// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Onvif.Open.Core.Abstract.Interface.Recording;
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
    public sealed class LiveViewService : IDisposable
    {
        #region Constructor

        public LiveViewService(IRecordingManager manager, LiveViewOptions options, TimeProvider timeProvider = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            Options = options ?? new LiveViewOptions();
            _timeProvider = timeProvider ?? TimeProvider.System;
            _manager.FrameReceived += OnFrame;
            _manager.AudioReceived += OnAudio;
            _cleanup = _timeProvider.CreateTimer(_ => RemoveIdle(), null, Options.IdleTimeout, Options.IdleTimeout);
        }

        #endregion

        #region Private Variables

        private readonly IRecordingManager _manager;
        private readonly TimeProvider _timeProvider;
        private readonly ITimer _cleanup;
        private readonly ConcurrentDictionary<string, Viewer> _viewers = new ConcurrentDictionary<string, Viewer>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public Variables

        public LiveViewOptions Options { get; }

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

        /// <summary>The low-latency stream of a camera, created on first use; null for unknown cameras.</summary>
        public Mp4LiveStream WatchMp4(string cameraId)
        {
            if (cameraId == null || !_manager.GetStatus().Any(s => string.Equals(s.CameraId, cameraId, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var viewer = Touch(cameraId);
            lock (viewer)
            {
                return viewer.Mp4 ??= new Mp4LiveStream();
            }
        }

        public void Dispose()
        {
            _manager.FrameReceived -= OnFrame;
            _manager.AudioReceived -= OnAudio;
            _cleanup.Dispose();
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

        private void RemoveIdle()
        {
            var now = _timeProvider.GetUtcNow().UtcTicks;

            foreach (var entry in _viewers)
            {
                if (now - Volatile.Read(ref entry.Value.LastAccessTicks) >= Options.IdleTimeout.Ticks && (entry.Value.Mp4?.ViewerCount ?? 0) == 0)
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
