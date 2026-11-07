// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Recording.Implementation.Storage;

namespace Onvif.Open.Recording.Implementation.Recorder
{
    /// <summary>
    /// Records one camera: connects, writes segments (rotating at key frames), and reconnects with
    /// exponential back-off when the stream fails, ends or stalls.
    /// </summary>
    internal sealed class CameraRecorder : IAsyncDisposable
    {
        #region Constructor

        public CameraRecorder(
            string cameraId,
            Func<CancellationToken, Task<ILiveVideoStream>> streamFactory,
            IRecordingStorage storage,
            ISegmentFormat format,
            IRotationPolicy rotationPolicy,
            RecorderOptions options,
            TimeProvider timeProvider,
            ILogger logger,
            Action<RecordingSegment> onSegmentCompleted,
            Action<CameraFrame> onFrame = null,
            Action<CameraStateChange> onStateChanged = null)
        {
            _onFrame = onFrame;
            _onStateChanged = onStateChanged;
            CameraId = cameraId;
            _streamFactory = streamFactory;
            _storage = storage;
            _format = format;
            _rotationPolicy = rotationPolicy;
            _options = options;
            _timeProvider = timeProvider;
            _logger = logger;
            _onSegmentCompleted = onSegmentCompleted;
            _status = new CameraRecordingStatus { CameraId = cameraId, State = RecordingState.Stopped };
        }

        #endregion

        #region Private Variables

        private readonly Func<CancellationToken, Task<ILiveVideoStream>> _streamFactory;
        private readonly IRecordingStorage _storage;
        private readonly ISegmentFormat _format;
        private readonly IRotationPolicy _rotationPolicy;
        private readonly RecorderOptions _options;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly Action<RecordingSegment> _onSegmentCompleted;
        private readonly Action<CameraFrame> _onFrame;
        private readonly Action<CameraStateChange> _onStateChanged;
        private readonly object _sync = new object();
        private readonly CameraRecordingStatus _status;
        private readonly SegmentProgress _progress = new SegmentProgress();

        private CancellationTokenSource _stop;
        private Task _run = Task.CompletedTask;

        private Stream _segmentStream;
        private ISegmentMuxer _muxer;
        private string _segmentPath;
        private TimeSpan _segmentFirstTimestamp;
        private VideoCodec _codec;

        #endregion

        #region Public Variables

        public string CameraId { get; }

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _stop != null;
                }
            }
        }

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
            await run.ConfigureAwait(false);
            stop.Dispose();
        }

        public CameraRecordingStatus GetStatus()
        {
            lock (_sync)
            {
                return _status.Clone();
            }
        }

        public ValueTask DisposeAsync()
        {
            return new ValueTask(StopAsync());
        }

        #endregion

        #region Private Methods

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var delay = _options.MinReconnectDelay;

            while (!cancellationToken.IsCancellationRequested)
            {
                var recordedFrames = false;
                SetState(RecordingState.Connecting);

                try
                {
                    var stream = await _streamFactory(cancellationToken).ConfigureAwait(false);

                    await using (stream.ConfigureAwait(false))
                    {
                        if (!_format.Supports(stream.Codec))
                        {
                            throw new NotSupportedException($"Codec '{stream.Codec}' cannot be recorded.");
                        }

                        _codec = stream.Codec;
                        Update(s =>
                        {
                            s.Codec = stream.Codec;
                            s.Transport = stream.Transport;
                            s.StreamUri = stream.StreamUri;
                        });
                        SetState(RecordingState.Recording);
                        _logger.LogInformation("Camera {CameraId}: recording {Codec} from {StreamUri}", CameraId, stream.Codec, stream.StreamUri);

                        recordedFrames = await RecordAsync(stream, cancellationToken).ConfigureAwait(false);
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        throw new IOException("The stream ended.");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    var now = _timeProvider.GetUtcNow();
                    Update(s =>
                    {
                        s.LastError = ex.Message;
                        s.LastErrorAt = now;
                    });
                    _logger.LogWarning(ex, "Camera {CameraId}: recording interrupted, reconnecting in {Delay}", CameraId, delay);
                }
                finally
                {
                    CloseSegment();
                }

                if (recordedFrames)
                {
                    delay = _options.MinReconnectDelay;
                }

                SetState(RecordingState.Reconnecting);

                try
                {
                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxReconnectDelay.Ticks));
            }

            SetState(RecordingState.Stopped);
            _logger.LogInformation("Camera {CameraId}: recording stopped", CameraId);
        }

        private async Task<bool> RecordAsync(ILiveVideoStream stream, CancellationToken cancellationToken)
        {
            var recorded = false;

            using (var watchdog = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                watchdog.CancelAfter(_options.StallTimeout);

                try
                {
                    await foreach (var frame in stream.ReadFramesAsync(watchdog.Token).ConfigureAwait(false))
                    {
                        watchdog.CancelAfter(_options.StallTimeout);
                        recorded |= WriteFrame(frame);
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"No video received for {_options.StallTimeout.TotalSeconds:0} seconds.");
                }
            }

            return recorded;
        }

        private bool WriteFrame(VideoFrame frame)
        {
            var now = _timeProvider.GetUtcNow();

            if (_muxer == null)
            {
                // A segment has to start with a key frame to be playable on its own.
                if (!frame.IsKeyFrame)
                {
                    return false;
                }

                OpenSegment(frame, now);
            }
            else if (frame.IsKeyFrame)
            {
                _progress.Now = now;
                _progress.Duration = frame.Timestamp - _segmentFirstTimestamp;
                _progress.Size = _segmentStream.Position;

                if (_rotationPolicy.ShouldRotate(_progress))
                {
                    CloseSegment();
                    OpenSegment(frame, now);
                }
                else
                {
                    // Limit data loss on power failure to roughly one GOP.
                    _segmentStream.Flush();
                }
            }

            var before = _segmentStream.Position;
            _muxer.Write(frame);
            var written = _segmentStream.Position - before;

            _progress.FrameCount++;
            Update(s =>
            {
                s.FramesRecorded++;
                s.BytesRecorded += written;
                s.LastFrameAt = now;
            });

            Notify(_onFrame, new CameraFrame(CameraId, frame, now), "frame");
            return true;
        }

        private void OpenSegment(VideoFrame frame, DateTimeOffset now)
        {
            _segmentStream = _storage.CreateSegment(CameraId, now, _format.GetFileExtension(_codec), out _segmentPath);
            _muxer = _format.CreateMuxer(_segmentStream, _codec);
            _segmentFirstTimestamp = frame.Timestamp;

            _progress.CameraId = CameraId;
            _progress.StartedAt = now;
            _progress.Now = now;
            _progress.Duration = TimeSpan.Zero;
            _progress.Size = 0;
            _progress.FrameCount = 0;

            var path = _segmentPath;
            Update(s => s.CurrentSegment = path);
            _logger.LogDebug("Camera {CameraId}: new segment {Path}", CameraId, path);
        }

        private void CloseSegment()
        {
            if (_muxer == null)
            {
                return;
            }

            var path = _segmentPath;
            long size = 0;

            try
            {
                _muxer.Dispose();
                size = _segmentStream.Position;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Camera {CameraId}: failed to finish segment {Path}", CameraId, path);
            }
            finally
            {
                _segmentStream.Dispose();
                _muxer = null;
                _segmentStream = null;
                _segmentPath = null;
            }

            Update(s =>
            {
                s.CurrentSegment = null;
                s.SegmentsCompleted++;
            });

            var segment = new RecordingSegment(CameraId, path, _progress.StartedAt, _timeProvider.GetUtcNow(), size, false);

            try
            {
                _onSegmentCompleted?.Invoke(segment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Camera {CameraId}: segment completed handler failed", CameraId);
            }
        }

        private void SetState(RecordingState state)
        {
            RecordingState previous;
            string error;

            lock (_sync)
            {
                previous = _status.State;
                _status.State = state;
                error = _status.LastError;
            }

            if (previous != state && _onStateChanged != null)
            {
                // Only report the error with the transition it caused.
                var reportedError = state == RecordingState.Reconnecting ? error : null;
                Notify(_onStateChanged, new CameraStateChange(CameraId, previous, state, reportedError, _timeProvider.GetUtcNow()), "state change");
            }
        }

        private void Notify<T>(Action<T> handler, T value, string what)
        {
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Camera {CameraId}: {What} handler failed", CameraId, what);
            }
        }

        private void Update(Action<CameraRecordingStatus> change)
        {
            lock (_sync)
            {
                change(_status);
            }
        }

        #endregion
    }
}
