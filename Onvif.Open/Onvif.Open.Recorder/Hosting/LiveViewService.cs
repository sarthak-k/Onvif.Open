// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Recording.Implementation.Hls;

namespace Onvif.Open.Recorder.Hosting
{
    /// <summary>
    /// Live HLS for the web viewer: a camera is packaged only while someone watches it (its playlist or segments
    /// were requested within <see cref="LiveViewOptions.IdleTimeout"/>), so idle cameras cost nothing.
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

            var viewer = _viewers.GetOrAdd(cameraId, _ => new Viewer(new HlsLiveStream(Options.SegmentDuration, Options.WindowSize)));
            Volatile.Write(ref viewer.LastAccessTicks, _timeProvider.GetUtcNow().UtcTicks);
            return viewer.Stream;
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

        private void OnFrame(object sender, CameraFrame frame)
        {
            if (_viewers.TryGetValue(frame.CameraId, out var viewer))
            {
                viewer.Stream.Add(frame.Frame);
            }
        }

        private void OnAudio(object sender, CameraAudio audio)
        {
            if (_viewers.TryGetValue(audio.CameraId, out var viewer))
            {
                viewer.Stream.Add(audio.Frame);
            }
        }

        private void RemoveIdle()
        {
            var now = _timeProvider.GetUtcNow().UtcTicks;

            foreach (var entry in _viewers)
            {
                if (now - Volatile.Read(ref entry.Value.LastAccessTicks) >= Options.IdleTimeout.Ticks)
                {
                    _viewers.TryRemove(entry.Key, out _);
                }
            }
        }

        #endregion

        private sealed class Viewer
        {
            public Viewer(HlsLiveStream stream)
            {
                Stream = stream;
            }

            public HlsLiveStream Stream { get; }
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
