// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Recording.Implementation.Hls;
using Onvif.Open.Recording.Implementation.Mp4;

namespace Onvif.Open.Recorder.Hosting
{
    /// <summary>
    /// Live video for browsers, packaged only while someone watches: low-latency fragmented MP4 (H.264, one fragment
    /// per frame, for Media Source Extensions) and HLS (any codec, with audio, a few seconds behind). Each format of a
    /// camera is made from the first request for it until nobody has asked for it for
    /// <see cref="LiveViewOptions.IdleTimeout"/> and no MP4 viewer is connected, so idle cameras cost nothing.
    /// </summary>
    public static class SubStreamStates
    {
        public const string Closed = "Closed";
        public const string Connecting = "Connecting";
        public const string Streaming = "Streaming";
        public const string Retrying = "Retrying";

        /// <summary>A camera's smaller stream for grids, read from the camera only while watched (docs/STATE-MACHINES.md).</summary>
        public static Onvif.Open.Core.StateMachines.StateMachine<string> Machine { get; } = new Onvif.Open.Core.StateMachines.StateMachine<string>(
            "Grid sub-stream",
            Closed,
            (Closed, Connecting, "a grid tile asks for it"),
            (Connecting, Streaming, "the camera sends frames"),
            (Connecting, Retrying, "could not connect"),
            (Streaming, Retrying, "the stream dropped"),
            (Retrying, Connecting, "after 1 s, doubling up to 30 s"),
            (Connecting, Closed, "nobody watches, the camera changed or is gone"),
            (Streaming, Closed, "nobody watches, the camera changed or is gone"),
            (Retrying, Closed, "nobody watches, the camera changed or is gone"));
    }

    public sealed class LiveViewService : IDisposable
    {
        #region Constructor

        /// <param name="findSubStream">Opens a camera's smaller stream for grids (null: it has none).</param>
        public LiveViewService(IRecordingManager manager, LiveViewOptions options, TimeProvider timeProvider = null, Func<string, Func<CancellationToken, Task<ILiveVideoStream>>> findSubStream = null, ILogger<LiveViewService> logger = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Options = options ?? new LiveViewOptions();
            _timeProvider = timeProvider ?? TimeProvider.System;
            _findSubStream = findSubStream ?? (_ => null);
            _logger = logger;
            _manager.FrameReceived += OnFrame;
            _manager.AudioReceived += OnAudio;
            _cleanup = _timeProvider.CreateTimer(_ => RemoveIdle(), null, Options.IdleTimeout, Options.IdleTimeout);
        }

        #endregion

        #region Private Variables

        private readonly IRecordingManager _manager;
        private readonly TimeProvider _timeProvider;
        private readonly Func<string, Func<CancellationToken, Task<ILiveVideoStream>>> _findSubStream;
        private readonly ILogger _logger;
        private readonly ITimer _cleanup;
        private readonly ConcurrentDictionary<string, Viewer> _viewers = new ConcurrentDictionary<string, Viewer>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public Variables

        public LiveViewOptions Options { get; }

        private static readonly TimeSpan SubStreamRetry = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan SubStreamMaxRetry = TimeSpan.FromSeconds(30);

        /// <summary>Cameras being packaged right now.</summary>
        public int ActiveCount => _viewers.Count;

        #endregion

        #region Public Methods

        /// <summary>The live stream of a camera, created on first use; null for unknown cameras.</summary>
        public HlsLiveStream Watch(string cameraId)
        {
            if (cameraId == null || !_manager.GetStatus().Any(s => string.Equals(s.CameraId, cameraId, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var viewer = Touch(cameraId);
            lock (viewer)
            {
                return viewer.Hls ??= new HlsLiveStream(Options.SegmentDuration, Options.WindowSize);
            }
        }

        /// <summary>
        /// The low-latency stream of a camera, created on first use; null for unknown cameras. With
        /// <paramref name="subStream"/>, its smaller stream for grids when it has one (opened now, closed when nobody
        /// watches it); <paramref name="isSubStream"/> says which one it is.
        /// </summary>
        public Mp4LiveStream WatchMp4(string cameraId, bool subStream, out bool isSubStream)
        {
            isSubStream = false;
            if (cameraId == null || !_manager.GetStatus().Any(s => string.Equals(s.CameraId, cameraId, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var viewer = Touch(cameraId);
            var open = subStream ? _findSubStream(cameraId) : null;
            lock (viewer)
            {
                if (open == null)
                {
                    return viewer.Mp4 ??= new Mp4LiveStream();
                }

                isSubStream = true;
                Volatile.Write(ref viewer.SubLastAccessTicks, _timeProvider.GetUtcNow().UtcTicks);
                if (viewer.Sub != null && !Equals(viewer.SubOpen, open))
                {
                    // The camera was changed (another stream, other credentials): its viewers reconnect to the new one.
                    StopSubStream(viewer);
                }

                if (viewer.Sub == null)
                {
                    viewer.SubState = SubStreamStates.Machine.Move(viewer.SubState, SubStreamStates.Connecting);
                    viewer.SubOpen = open;
                    viewer.Sub = new Mp4LiveStream();
                    viewer.SubReader = new CancellationTokenSource();
                    var stream = viewer.Sub;
                    var stop = viewer.SubReader.Token;
                    _ = Task.Run(() => ReadSubStreamAsync(cameraId, viewer, open, stream, stop));
                }

                return viewer.Sub;
            }
        }

        public Mp4LiveStream WatchMp4(string cameraId) => WatchMp4(cameraId, false, out _);

        /// <summary>Where a camera's smaller stream stands (see <see cref="SubStreamStates"/>).</summary>
        public string SubStreamState(string cameraId) => cameraId != null && _viewers.TryGetValue(cameraId, out var viewer) ? viewer.SubState : SubStreamStates.Closed;

        /// <summary>Cameras whose smaller stream is open right now.</summary>
        public int OpenSubStreamCount => _viewers.Values.Count(v => v.Sub != null);

        public void Dispose()
        {
            _manager.FrameReceived -= OnFrame;
            _manager.AudioReceived -= OnAudio;
            _cleanup.Dispose();
            foreach (var viewer in _viewers.Values)
            {
                StopSubStream(viewer);
            }

            _viewers.Clear();
        }

        #endregion

        #region Private Methods

        private Viewer Touch(string cameraId)
        {
            var viewer = _viewers.GetOrAdd(cameraId, _ => new Viewer());
            Volatile.Write(ref viewer.LastAccessTicks, _timeProvider.GetUtcNow().UtcTicks);
            return viewer;
        }

        internal void OnFrame(object sender, CameraFrame frame)
        {
            if (_viewers.TryGetValue(frame.CameraId, out var viewer))
            {
                viewer.Hls?.Add(frame.Frame);
                viewer.Mp4?.Add(frame.Frame);
            }
        }

        private void OnAudio(object sender, CameraAudio audio)
        {
            if (_viewers.TryGetValue(audio.CameraId, out var viewer))
            {
                viewer.Hls?.Add(audio.Frame);
            }
        }

        /// <summary>Reads a camera's smaller stream while someone watches it, reconnecting when it drops.</summary>
        private async Task ReadSubStreamAsync(string cameraId, Viewer viewer, Func<CancellationToken, Task<ILiveVideoStream>> open, Mp4LiveStream target, CancellationToken stop)
        {
            // Moves of this reader only, and only while it is the current one (a stop moves to Closed itself).
            void Move(string to)
            {
                lock (viewer)
                {
                    if (!stop.IsCancellationRequested && viewer.SubState != to)
                    {
                        viewer.SubState = SubStreamStates.Machine.Move(viewer.SubState, to);
                    }
                }
            }

            var delay = SubStreamRetry;
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    await using (var stream = await open(stop).ConfigureAwait(false))
                    {
                        await foreach (var frame in stream.ReadFramesAsync(stop).ConfigureAwait(false))
                        {
                            Move(SubStreamStates.Streaming);
                            target.Add(frame);
                            delay = SubStreamRetry;
                        }
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Camera {CameraId}: the stream for grids failed ({Error}); retrying in {Delay}", cameraId, ex.Message, delay);
                }

                Move(SubStreamStates.Retrying);
                try
                {
                    await Task.Delay(delay, _timeProvider, stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                Move(SubStreamStates.Connecting);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, SubStreamMaxRetry.Ticks));
            }
        }

        private static void StopSubStream(Viewer viewer)
        {
            lock (viewer)
            {
                if (viewer.SubState != SubStreamStates.Closed)
                {
                    viewer.SubState = SubStreamStates.Machine.Move(viewer.SubState, SubStreamStates.Closed);
                }

                viewer.SubReader?.Cancel();
                viewer.SubReader?.Dispose();
                viewer.SubReader = null;
                viewer.SubOpen = null;
                viewer.Sub?.Close();
                viewer.Sub = null;
            }
        }

        internal void RemoveIdle()
        {
            var now = _timeProvider.GetUtcNow().UtcTicks;
            var cameras = _manager.GetStatus().Select(s => s.CameraId).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _viewers)
            {
                // The smaller stream is a connection to the camera: closed as soon as nobody watches it, or the camera is gone.
                if (entry.Value.Sub != null && (!cameras.Contains(entry.Key) || (now - Volatile.Read(ref entry.Value.SubLastAccessTicks) >= Options.IdleTimeout.Ticks && entry.Value.Sub.ViewerCount == 0)))
                {
                    StopSubStream(entry.Value);
                }

                if (now - Volatile.Read(ref entry.Value.LastAccessTicks) >= Options.IdleTimeout.Ticks && (entry.Value.Mp4?.ViewerCount ?? 0) == 0 && entry.Value.Sub == null)
                {
                    _viewers.TryRemove(entry.Key, out _);
                }
            }
        }

        #endregion

        private sealed class Viewer
        {
            public HlsLiveStream Hls;
            public Mp4LiveStream Mp4;
            public long LastAccessTicks;

            /// <summary>The smaller stream for grids, read from the camera only while watched.</summary>
            public Mp4LiveStream Sub;
            public CancellationTokenSource SubReader;
            public Func<CancellationToken, Task<ILiveVideoStream>> SubOpen;
            public string SubState = SubStreamStates.Closed;
            public long SubLastAccessTicks;
        }
    }

    /// <summary>
    /// "LiveView" section: browser live view (HLS).
    /// </summary>
    public sealed class LiveViewOptions
    {
        #region Public Variables

        public const string SectionName = "LiveView";

        /// <summary>Target length of the live segments (cut at key frames; at least one GOP). Default: 2 s.</summary>
        public TimeSpan SegmentDuration { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>Segments in the live playlist. Default: 6.</summary>
        public int WindowSize { get; set; } = 6;

        /// <summary>Stop packaging a camera this long after its last request. Default: 1 minute.</summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>How long the first playlist request waits for video. Default: 15 s.</summary>
        public TimeSpan StartTimeout { get; set; } = TimeSpan.FromSeconds(15);

        #endregion
    }
}
