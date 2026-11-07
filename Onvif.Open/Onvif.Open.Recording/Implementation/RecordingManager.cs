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
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Recording;
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
            ILoggerFactory loggerFactory)
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

        #endregion

        #region Public Methods

        public void AddCamera(string cameraId, ICamera camera, string profileToken = null, StreamTransport transport = StreamTransport.Rtsp, IRotationPolicy rotationPolicy = null)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            var media = camera.InitializeMedia();
            AddCamera(cameraId, ct => media.StartLiveStreamAsync(profileToken, transport, ct), rotationPolicy);
        }

        public void AddCamera(string cameraId, Func<CancellationToken, Task<ILiveVideoStream>> streamFactory, IRotationPolicy rotationPolicy = null)
        {
            CameraIdentifier.Require(cameraId);

            if (streamFactory == null)
            {
                throw new ArgumentNullException(nameof(streamFactory));
            }

            var recorder = new CameraRecorder(
                cameraId,
                streamFactory,
                Storage,
                _format,
                rotationPolicy ?? _rotationPolicy,
                _recorderOptions,
                _timeProvider,
                _loggerFactory.CreateLogger($"{typeof(CameraRecorder).FullName}.{cameraId}"),
                OnSegmentCompleted);

            if (!_recorders.TryAdd(cameraId, recorder))
            {
                throw new ArgumentException($"Camera '{cameraId}' has already been added.", nameof(cameraId));
            }

            if (IsStarted)
            {
                recorder.Start();
            }
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

            foreach (var recorder in _recorders.Values)
            {
                recorder.Start();
            }

            _logger.LogInformation("Recording started for {Count} camera(s) into {Storage}", _recorders.Count, Storage);
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
            return Storage.GetSegments(cameraId);
        }

        public bool DeleteSegment(RecordingSegment segment)
        {
            if (segment == null)
            {
                throw new ArgumentNullException(nameof(segment));
            }

            return !segment.IsActive && Storage.Delete(segment);
        }

        public StorageInformation GetStorageInformation()
        {
            return new StorageInformation(Storage.ToString(), Storage.GetAvailableFreeBytes());
        }

        public async Task<IReadOnlyList<RecordingSegment>> ApplyRetentionAsync(CancellationToken cancellationToken = default)
        {
            await _retentionLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                return await Task.Run(() =>
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

                    return (IReadOnlyList<RecordingSegment>)deleted;
                }, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _retentionLock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
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
            ILoggerFactory loggerFactory)
        {
            return new RecordingManager(storage, format, rotationPolicy, retentionPolicy, retentionInterval, recorderOptions, timeProvider, loggerFactory);
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

        private void OnSegmentCompleted(RecordingSegment segment)
        {
            _retentionSignal.Writer.TryWrite(true);

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
