// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Recording;
using Onvif.Open.Core.Storage;
using Onvif.Open.Recording.Implementation.Storage;

namespace Onvif.Open.Recording.Implementation.Archive
{
    /// <summary>
    /// Uploads completed local segments to an <see cref="ISegmentStore"/> and keeps an index of what is archived.
    /// Failed uploads are retried with back-off; segments not uploaded before a restart are found again on the
    /// local disk, so nothing depends on in-memory state.
    /// </summary>
    internal sealed class SegmentArchive : IAsyncDisposable
    {
        #region Constructor

        public SegmentArchive(ISegmentStore store, SegmentArchiveOptions options, IRecordingStorage localStorage, TimeProvider timeProvider, ILogger logger, Action uploaded)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Options = (options ?? new SegmentArchiveOptions()).Normalize();
            _localStorage = localStorage;
            _timeProvider = timeProvider;
            _logger = logger;
            _uploaded = uploaded ?? (() => { });
        }

        #endregion

        #region Private Variables

        private const int ReadBufferSize = 256 * 1024;

        private readonly IRecordingStorage _localStorage;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly Action _uploaded;

        // Keys without prefix.
        private readonly ConcurrentDictionary<string, StoredObject> _index = new ConcurrentDictionary<string, StoredObject>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, RecordingSegment> _pending = new ConcurrentDictionary<string, RecordingSegment>(StringComparer.Ordinal);
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();

        private Channel<string> _queue;
        private CancellationTokenSource _stop;
        private Task _run = Task.CompletedTask;
        private volatile bool _isAvailable;
        private DateTimeOffset? _lastUploadAt;
        private string _lastError;
        private DateTimeOffset? _lastErrorAt;

        #endregion

        #region Public Variables

        public ISegmentStore Store { get; }

        public SegmentArchiveOptions Options { get; }

        /// <summary>True once the store has been listed: before that, nothing is known to be archived.</summary>
        public bool IsAvailable => _isAvailable;

        #endregion

        #region Public Methods

        public void Start()
        {
            lock (_sync)
            {
                if (_stop != null)
                {
                    return;
                }

                _stop = new CancellationTokenSource();
                _queue = Channel.CreateUnbounded<string>();

                // Segments completed while stopped are found again when the local disk is scanned.
                _pending.Clear();
                var token = _stop.Token;
                _run = Task.Run(() => RunAsync(token));
            }
        }

        public async Task StopAsync()
        {
            CancellationTokenSource stop;
            Task run;

            lock (_sync)
            {
                stop = _stop;
                run = _run;
                _stop = null;
            }

            if (stop == null)
            {
                return;
            }

            stop.Cancel();

            try
            {
                await run.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            stop.Dispose();
        }

        /// <summary>Queues a completed local segment for upload (ignored when already archived or queued).</summary>
        public void Enqueue(RecordingSegment segment)
        {
            if (segment == null || segment.IsActive || !segment.IsLocal)
            {
                return;
            }

            var key = SegmentNames.ArchiveKey(segment);
            Channel<string> queue;

            lock (_sync)
            {
                queue = _queue;
            }

            if (queue != null && !_index.ContainsKey(key) && _pending.TryAdd(key, segment))
            {
                queue.Writer.TryWrite(key);
            }
        }

        public bool Contains(RecordingSegment segment)
        {
            return _index.ContainsKey(SegmentNames.ArchiveKey(segment));
        }

        /// <summary>Archived segments (as archive entries), oldest first.</summary>
        public IReadOnlyList<RecordingSegment> GetSegments(string cameraId = null)
        {
            var segments = new List<(RecordingSegment Segment, int Sequence)>();

            foreach (var entry in _index)
            {
                if (SegmentNames.TryParseArchiveKey(entry.Key, out var id, out _, out var start, out var sequence)
                    && (cameraId == null || string.Equals(id, cameraId, StringComparison.OrdinalIgnoreCase)))
                {
                    segments.Add((new RecordingSegment(id, entry.Key, start, entry.Value.LastModified, entry.Value.Size, false, isLocal: false, isArchived: true), sequence));
                }
            }

            return segments
                .OrderBy(s => s.Segment.StartTime)
                .ThenBy(s => s.Segment.CameraId, StringComparer.Ordinal)
                .ThenBy(s => s.Sequence)
                .Select(s => s.Segment)
                .ToList();
        }

        public Task<Stream> OpenReadAsync(RecordingSegment segment, CancellationToken cancellationToken)
        {
            return Store.OpenReadAsync(Options.Prefix + SegmentNames.ArchiveKey(segment), cancellationToken);
        }

        public async Task<bool> DeleteAsync(RecordingSegment segment, CancellationToken cancellationToken)
        {
            var key = SegmentNames.ArchiveKey(segment);
            _pending.TryRemove(key, out _);

            var deleted = await Store.DeleteAsync(Options.Prefix + key, cancellationToken).ConfigureAwait(false);
            _index.TryRemove(key, out _);
            return deleted;
        }

        /// <summary>Stops trying to upload a segment whose local file is about to be removed.</summary>
        public void Forget(RecordingSegment segment)
        {
            _pending.TryRemove(SegmentNames.ArchiveKey(segment), out _);
        }

        public ArchiveInformation GetInformation()
        {
            return new ArchiveInformation(
                Options.Name ?? Store.GetType().Name,
                Options.Prefix.Length == 0 ? Store.ToString() : $"{Store} ({Options.Prefix})",
                _isAvailable,
                _pending.Count,
                _lastUploadAt,
                _lastError,
                _lastErrorAt);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);

            switch (Store)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        #endregion

        #region Private Methods

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            // Nothing can be uploaded safely before we know what is there already.
            await WithRetryAsync("list the archive", ct => RefreshAsync(ct), cancellationToken).ConfigureAwait(false);
            _isAvailable = true;

            foreach (var segment in _localStorage.GetSegments().Where(s => !s.IsActive))
            {
                Enqueue(segment);
            }

            var workers = Enumerable.Range(0, Options.MaxConcurrentUploads).Select(_ => UploadLoopAsync(cancellationToken)).ToList();
            workers.Add(RefreshLoopAsync(cancellationToken));
            await Task.WhenAll(workers).ConfigureAwait(false);
        }

        private async Task UploadLoopAsync(CancellationToken cancellationToken)
        {
            Channel<string> queue;
            lock (_sync)
            {
                queue = _queue;
            }

            await foreach (var key in queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_pending.TryGetValue(key, out var segment))
                {
                    continue;
                }

                try
                {
                    if (!_index.ContainsKey(key))
                    {
                        await WithRetryAsync($"upload {key}", ct => UploadAsync(key, segment, ct), cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _pending.TryRemove(key, out _);
                }
            }
        }

        private async Task UploadAsync(string key, RecordingSegment segment, CancellationToken cancellationToken)
        {
            if (!_pending.ContainsKey(key))
            {
                // Deleted while waiting for a retry.
                return;
            }

            FileStream content;
            try
            {
                content = new FileStream(segment.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, ReadBufferSize, useAsync: true);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                // Deleted locally meanwhile (by the user or to free space): nothing left to upload.
                _logger.LogWarning("Not archiving {Key}: the local file no longer exists", key);
                return;
            }

            using (content)
            {
                var size = content.Length;
                await Store.UploadAsync(Options.Prefix + key, content, cancellationToken).ConfigureAwait(false);

                var now = _timeProvider.GetUtcNow();
                _index[key] = new StoredObject(Options.Prefix + key, size, now);
                _lastUploadAt = now;
                _logger.LogInformation("Archived {Key} ({Size} bytes)", key, size);
            }

            _uploaded();
        }

        private async Task RefreshLoopAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                await Task.Delay(Options.RefreshInterval, _timeProvider, cancellationToken).ConfigureAwait(false);

                try
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    RecordError("list the archive", ex);
                }
            }
        }

        /// <summary>Lists the store: adds what is there and removes what is gone (unless it was uploaded meanwhile).</summary>
        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            var started = _timeProvider.GetUtcNow();
            var found = new HashSet<string>(StringComparer.Ordinal);

            await foreach (var stored in Store.ListAsync(Options.Prefix, cancellationToken).ConfigureAwait(false))
            {
                if (stored?.Key == null || !stored.Key.StartsWith(Options.Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var key = stored.Key.Substring(Options.Prefix.Length);

                if (SegmentNames.TryParseArchiveKey(key, out _, out _, out _, out _))
                {
                    found.Add(key);
                    _index[key] = stored;
                }
            }

            foreach (var entry in _index)
            {
                if (!found.Contains(entry.Key) && entry.Value.LastModified < started)
                {
                    _index.TryRemove(entry.Key, out _);
                }
            }
        }

        private async Task WithRetryAsync(string what, Func<CancellationToken, Task> action, CancellationToken cancellationToken)
        {
            var delay = Options.MinRetryDelay;

            while (true)
            {
                try
                {
                    await action(cancellationToken).ConfigureAwait(false);
                    return;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
                {
                    RecordError(what, ex);
                }

                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, Options.MaxRetryDelay.Ticks));
            }
        }

        private void RecordError(string what, Exception ex)
        {
            _lastError = $"Could not {what}: {ex.Message}";
            _lastErrorAt = _timeProvider.GetUtcNow();
            _logger.LogWarning(ex, "Could not {What}; retrying", what);
        }

        #endregion
    }
}
