// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recording.Implementation.Archive;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Recording.Implementation.Recorder;
using Onvif.Open.Recording.Implementation.Storage;

namespace Onvif.Open.Recording.Implementation
{
    /// <summary>
    /// Records many cameras into one storage and enforces the retention policy.
    /// Build it with <see cref="Builder.RecordingManagerBuilder"/>.
    /// </summary>
    public sealed class RecordingManager : IRecordingManager
    {
        #region Constructor

        private RecordingManager(
            IRecordingStorage storage,
            ISegmentFormat format,
            IRotationPolicy rotationPolicy,
            IRetentionPolicy retentionPolicy,
            TimeSpan retentionInterval,
            RecorderOptions recorderOptions,
            TimeProvider timeProvider,
            ILoggerFactory loggerFactory,
            ISegmentStore archiveStore,
            SegmentArchiveOptions archiveOptions)
        {
            Storage = storage;
            _format = format;
            _rotationPolicy = rotationPolicy;
            _retentionPolicy = retentionPolicy;
            _retentionInterval = retentionInterval;
            _recorderOptions = recorderOptions;
            _timeProvider = timeProvider;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<RecordingManager>();

            if (archiveStore != null)
            {
                _archive = new SegmentArchive(archiveStore, archiveOptions, storage, timeProvider, loggerFactory.CreateLogger<SegmentArchive>(), () => _retentionSignal.Writer.TryWrite(true));
            }
        }

        #endregion

        #region Private Variables

        private readonly ISegmentFormat _format;
        private readonly IRotationPolicy _rotationPolicy;
        private readonly IRetentionPolicy _retentionPolicy;
        private readonly TimeSpan _retentionInterval;
        private readonly RecorderOptions _recorderOptions;
        private readonly TimeProvider _timeProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly SegmentArchive _archive;
        private readonly ConcurrentDictionary<string, CameraRecorder> _recorders = new ConcurrentDictionary<string, CameraRecorder>(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _retentionLock = new SemaphoreSlim(1, 1);
        private readonly Channel<bool> _retentionSignal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        private readonly object _sync = new object();

        private CancellationTokenSource _stop;
        private Task _retentionLoop = Task.CompletedTask;

        #endregion

        #region Public Variables

        public IRecordingStorage Storage { get; }

        public bool IsStarted
        {
            get
            {
                lock (_sync)
                {
                    return _stop != null;
                }
            }
        }

        public event EventHandler<RecordingSegment> SegmentCompleted;

        public event EventHandler<CameraFrame> FrameReceived;

        public event EventHandler<CameraStateChange> CameraStateChanged;

        public event EventHandler<CameraEvent> CameraEventReceived;

        public event EventHandler<AnalyticsFrame> AnalyticsReceived;

        #endregion

        #region Public Methods

        public void AddCamera(string cameraId, ICamera camera, string profileToken = null, StreamTransport transport = StreamTransport.Rtsp, IRotationPolicy rotationPolicy = null)
        {
            AddCamera(cameraId, camera, new CameraRecordingOptions { ProfileToken = profileToken, Transport = transport, RotationPolicy = rotationPolicy });
        }

        public void AddCamera(string cameraId, ICamera camera, CameraRecordingOptions options)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            options = options ?? new CameraRecordingOptions();
            var media = camera.InitializeMedia();
            var events = options.SubscribeToEvents ? camera.InitializeEvents() : null;

            AddCamera(
                cameraId,
                ct => media.StartLiveStreamAsync(options.ProfileToken, options.Transport, ct),
                options,
                events == null ? (Func<CancellationToken, IAsyncEnumerable<CameraEvent>>)null : ct => events.ReadEventsAsync(ct),
                options.UsesAnalytics ? ct => ReadAnalyticsAsync(media, options.AnalyticsProfileToken, options.Transport, ct) : (Func<CancellationToken, IAsyncEnumerable<AnalyticsFrame>>)null);
        }

        public void AddCamera(string cameraId, Func<CancellationToken, Task<ILiveVideoStream>> streamFactory, IRotationPolicy rotationPolicy = null)
        {
            AddCamera(cameraId, streamFactory, new CameraRecordingOptions { RotationPolicy = rotationPolicy });
        }

        public void AddCamera(string cameraId, Func<CancellationToken, Task<ILiveVideoStream>> streamFactory, CameraRecordingOptions options, Func<CancellationToken, IAsyncEnumerable<CameraEvent>> eventSource = null, Func<CancellationToken, IAsyncEnumerable<AnalyticsFrame>> analyticsSource = null)
        {
            CameraIdentifier.Require(cameraId);

            if (streamFactory == null)
            {
                throw new ArgumentNullException(nameof(streamFactory));
            }

            options = options ?? new CameraRecordingOptions();

            var recorder = new CameraRecorder(
                cameraId,
                streamFactory,
                Storage,
                _format,
                options.RotationPolicy ?? _rotationPolicy,
                _recorderOptions,
                _timeProvider,
                _loggerFactory.CreateLogger($"{typeof(CameraRecorder).FullName}.{cameraId}"),
                OnSegmentCompleted,
                frame => FrameReceived?.Invoke(this, frame),
                change => CameraStateChanged?.Invoke(this, change),
                options,
                eventSource,
                cameraEvent => CameraEventReceived?.Invoke(this, cameraEvent),
                analyticsSource,
                frame => AnalyticsReceived?.Invoke(this, frame));

            if (!_recorders.TryAdd(cameraId, recorder))
            {
                throw new ArgumentException($"Camera '{cameraId}' has already been added.", nameof(cameraId));
            }

            if (IsStarted)
            {
                recorder.Start();
            }
        }

        public void TriggerRecording(string cameraId, TimeSpan duration)
        {
            GetRecorder(cameraId).Trigger(duration);
        }

        public async Task<bool> RemoveCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            if (cameraId == null || !_recorders.TryRemove(cameraId, out var recorder))
            {
                return false;
            }

            await recorder.DisposeAsync().ConfigureAwait(false);
            return true;
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_stop != null)
                {
                    return Task.CompletedTask;
                }

                _stop = new CancellationTokenSource();
                var token = _stop.Token;
                _retentionLoop = Task.Run(() => RetentionLoopAsync(token));
            }

            _archive?.Start();

            foreach (var recorder in _recorders.Values)
            {
                recorder.Start();
            }

            _logger.LogInformation("Recording started for {Count} camera(s) into {Storage}{Archive}", _recorders.Count, Storage, _archive == null ? string.Empty : ", archived to " + _archive.GetInformation().Location);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokenSource stop;
            Task retentionLoop;

            lock (_sync)
            {
                stop = _stop;
                retentionLoop = _retentionLoop;
                _stop = null;
            }

            await Task.WhenAll(_recorders.Values.Select(r => r.StopAsync())).ConfigureAwait(false);

            if (_archive != null)
            {
                await _archive.StopAsync().ConfigureAwait(false);
            }

            if (stop != null)
            {
                stop.Cancel();
                await retentionLoop.ConfigureAwait(false);
                stop.Dispose();
            }
        }

        public Task StartCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            GetRecorder(cameraId).Start();
            return Task.CompletedTask;
        }

        public Task StopCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            return GetRecorder(cameraId).StopAsync();
        }

        public IReadOnlyList<CameraRecordingStatus> GetStatus()
        {
            return _recorders.Values.Select(r => r.GetStatus()).OrderBy(s => s.CameraId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public IReadOnlyList<RecordingSegment> GetSegments(string cameraId = null)
        {
            var local = Storage.GetSegments(cameraId);

            if (_archive == null)
            {
                return local;
            }

            var merged = new Dictionary<string, RecordingSegment>(StringComparer.Ordinal);

            foreach (var segment in _archive.GetSegments(cameraId))
            {
                merged[SegmentNames.ArchiveKey(segment)] = segment;
            }

            foreach (var segment in local)
            {
                var key = SegmentNames.ArchiveKey(segment);
                merged[key] = merged.ContainsKey(key)
                    ? new RecordingSegment(segment.CameraId, segment.Path, segment.StartTime, segment.LastWriteTime, segment.Size, segment.IsActive, isLocal: true, isArchived: true)
                    : segment;
            }

            return merged.Values
                .OrderBy(s => s.StartTime)
                .ThenBy(s => s.CameraId, StringComparer.Ordinal)
                .ThenBy(SegmentNames.Sequence)
                .ToList();
        }

        public async Task<System.IO.Stream> OpenSegmentAsync(RecordingSegment segment, CancellationToken cancellationToken = default)
        {
            // Look the segment up rather than trusting its Path: callers (plugins) must not open arbitrary files.
            var known = Find(segment);

            if (known == null)
            {
                return null;
            }

            if (known.IsLocal)
            {
                try
                {
                    return new System.IO.FileStream(known.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite | System.IO.FileShare.Delete, 64 * 1024, useAsync: true);
                }
                catch (Exception ex) when ((ex is System.IO.FileNotFoundException || ex is System.IO.DirectoryNotFoundException) && known.IsArchived)
                {
                    // Removed locally after being archived: read the archived copy.
                }
            }

            return known.IsArchived ? await _archive.OpenReadAsync(known, cancellationToken).ConfigureAwait(false) : null;
        }

        public async Task<bool> DeleteSegmentAsync(RecordingSegment segment, CancellationToken cancellationToken = default)
        {
            var known = Find(segment);

            if (known == null || known.IsActive)
            {
                return false;
            }

            return await DeleteEverywhereAsync(known, cancellationToken).ConfigureAwait(false);
        }

        public StorageInformation GetStorageInformation()
        {
            return new StorageInformation(Storage.ToString(), Storage.GetAvailableFreeBytes(), _archive?.GetInformation());
        }

        public async Task<IReadOnlyList<RecordingSegment>> ApplyRetentionAsync(CancellationToken cancellationToken = default)
        {
            await _retentionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return _archive == null
                    ? await Task.Run(() => ApplyLocalRetention(cancellationToken), cancellationToken).ConfigureAwait(false)
                    : await ApplyArchiveRetentionAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _retentionLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);

            if (_archive != null)
            {
                await _archive.DisposeAsync().ConfigureAwait(false);
            }

            _retentionLock.Dispose();
        }

        internal static RecordingManager CreateInstance(
            IRecordingStorage storage,
            ISegmentFormat format,
            IRotationPolicy rotationPolicy,
            IRetentionPolicy retentionPolicy,
            TimeSpan retentionInterval,
            RecorderOptions recorderOptions,
            TimeProvider timeProvider,
            ILoggerFactory loggerFactory,
            ISegmentStore archiveStore = null,
            SegmentArchiveOptions archiveOptions = null)
        {
            return new RecordingManager(storage, format, rotationPolicy, retentionPolicy, retentionInterval, recorderOptions, timeProvider, loggerFactory, archiveStore, archiveOptions);
        }

        #endregion

        #region Private Methods

        private CameraRecorder GetRecorder(string cameraId)
        {
            if (cameraId == null || !_recorders.TryGetValue(cameraId, out var recorder))
            {
                throw new KeyNotFoundException($"Camera '{cameraId}' is not registered.");
            }

            return recorder;
        }

        private static async IAsyncEnumerable<AnalyticsFrame> ReadAnalyticsAsync(ICameraMedia media, string profileToken, StreamTransport transport, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await using (var stream = await media.StartAnalyticsStreamAsync(profileToken, transport, cancellationToken).ConfigureAwait(false))
            {
                await foreach (var frame in stream.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return frame;
                }
            }
        }

        private RecordingSegment Find(RecordingSegment segment)
        {
            if (segment == null)
            {
                throw new ArgumentNullException(nameof(segment));
            }

            return GetSegments(segment.CameraId).FirstOrDefault(s =>
                string.Equals(s.CameraId, segment.CameraId, StringComparison.OrdinalIgnoreCase) && string.Equals(s.FileName, segment.FileName, StringComparison.Ordinal));
        }

        private async Task<bool> DeleteEverywhereAsync(RecordingSegment segment, CancellationToken cancellationToken)
        {
            var deleted = false;

            if (segment.IsLocal)
            {
                _archive?.Forget(segment);
                deleted = Storage.Delete(segment);
            }

            if (segment.IsArchived && _archive != null)
            {
                deleted |= await _archive.DeleteAsync(segment, cancellationToken).ConfigureAwait(false);
            }

            return deleted;
        }

        private IReadOnlyList<RecordingSegment> ApplyLocalRetention(CancellationToken cancellationToken)
        {
            var completed = Storage.GetSegments().Where(s => !s.IsActive).ToList();
            var context = new RetentionContext(completed, _timeProvider.GetUtcNow(), Storage.GetAvailableFreeBytes());
            var deleted = new List<RecordingSegment>();

            foreach (var segment in _retentionPolicy.SelectForDeletion(context).Where(s => s != null && !s.IsActive).Distinct().ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Storage.Delete(segment))
                {
                    deleted.Add(segment);
                    _logger.LogInformation("Retention: deleted {Path} ({Size} bytes)", segment.Path, segment.Size);
                }
            }

            return deleted;
        }

        /// <summary>
        /// With an archive, the retention policy decides how long recordings are kept in the archive, and the
        /// local disk is a buffer: uploaded copies are removed after <see cref="SegmentArchiveOptions.KeepLocalCopyFor"/>,
        /// and anything (oldest first, uploaded copies first) when the disk runs out of space.
        /// </summary>
        private async Task<IReadOnlyList<RecordingSegment>> ApplyArchiveRetentionAsync(CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow();
            var deleted = new List<RecordingSegment>();

            if (_archive.IsAvailable)
            {
                var archived = _archive.GetSegments();
                var context = new RetentionContext(archived, now, null);

                foreach (var selected in _retentionPolicy.SelectForDeletion(context).Where(s => s != null).Distinct().ToList())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var segment = Find(selected);

                    if (segment != null && !segment.IsActive && await DeleteEverywhereAsync(segment, cancellationToken).ConfigureAwait(false))
                    {
                        deleted.Add(segment);
                        _logger.LogInformation("Retention: deleted {Path} ({Size} bytes) from the archive", SegmentNames.ArchiveKey(segment), segment.Size);
                    }
                }
            }

            var local = Storage.GetSegments().Where(s => !s.IsActive).ToList();
            var removed = new HashSet<RecordingSegment>();

            foreach (var segment in local.Where(s => _archive.Contains(s) && now - s.LastWriteTime >= _archive.Options.KeepLocalCopyFor))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (Storage.Delete(segment))
                {
                    removed.Add(segment);
                    _logger.LogDebug("Removed local copy of archived {Path}", segment.Path);
                }
            }

            var free = Storage.GetAvailableFreeBytes();

            if (free.HasValue && free.Value < _archive.Options.MinLocalFreeBytes)
            {
                foreach (var segment in local.Where(s => !removed.Contains(s)).OrderBy(s => _archive.Contains(s) ? 0 : 1).ThenBy(s => s.StartTime))
                {
                    if (free.Value >= _archive.Options.MinLocalFreeBytes)
                    {
                        break;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var archived = _archive.Contains(segment);

                    if (!archived)
                    {
                        _archive.Forget(segment);
                    }

                    if (Storage.Delete(segment))
                    {
                        free += segment.Size;

                        if (!archived)
                        {
                            deleted.Add(segment);
                            _logger.LogWarning("Low disk space: deleted {Path} ({Size} bytes) before it could be archived", segment.Path, segment.Size);
                        }
                    }
                }
            }

            return deleted;
        }

        private void OnSegmentCompleted(RecordingSegment segment)
        {
            _retentionSignal.Writer.TryWrite(true);
            _archive?.Enqueue(segment);

            var handler = SegmentCompleted;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(this, segment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SegmentCompleted handler failed for {Path}", segment.Path);
            }
        }

        private async Task RetentionLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ApplyRetentionAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Retention failed");
                }

                // Wake up periodically, or as soon as a segment was completed.
                using (var wake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    var delay = Task.Delay(_retentionInterval, _timeProvider, wake.Token);
                    var signal = _retentionSignal.Reader.WaitToReadAsync(wake.Token).AsTask();

                    try
                    {
                        await Task.WhenAny(delay, signal).ConfigureAwait(false);
                    }
                    finally
                    {
                        wake.Cancel();
                    }

                    while (_retentionSignal.Reader.TryRead(out _))
                    {
                    }
                }
            }
        }

        #endregion
    }
}
