// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Recording.Implementation.Mp4
{
    /// <summary>
    /// Low-latency live video for browsers: each frame of a camera becomes a fragmented-MP4 fragment the moment it
    /// arrives, and goes to every viewer. A new viewer gets the init segment and the frames since the last key frame,
    /// so it shows the picture at once. A viewer that falls behind is dropped (its player reconnects at the live
    /// edge); a new stream (other parameter sets, time going backwards) drops every viewer the same way.
    /// H.264 only: for other codecs <see cref="IsSupported"/> is false and players use HLS.
    /// </summary>
    public sealed class Mp4LiveStream
    {
        #region Constructor

        public Mp4LiveStream(int viewerBacklog = 200, int maxCachedFrames = 600)
        {
            _viewerBacklog = viewerBacklog;
            _maxCachedFrames = maxCachedFrames;
        }

        #endregion

        #region Private Variables

        private const uint DefaultDuration = FragmentedMp4.Timescale / 25;

        private readonly int _viewerBacklog;
        private readonly int _maxCachedFrames;
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();
        private readonly List<byte[]> _gop = new List<byte[]>();
        private readonly HashSet<Viewer> _viewers = new HashSet<Viewer>();
        private TaskCompletionSource<bool> _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private byte[] _init;
        private byte[] _sps;
        private byte[] _pps;
        private TimeSpan? _first;
        private TimeSpan _last;
        private uint _duration = DefaultDuration;
        private uint _sequence;

        #endregion

        #region Public Variables

        public VideoCodec Codec { get; private set; }

        /// <summary>False once the camera sends something other than H.264.</summary>
        public bool IsSupported => Codec is VideoCodec.Unknown or VideoCodec.H264;

        /// <summary>The RFC 6381 codec, e.g. "avc1.64001f"; null until the first key frame.</summary>
        public string CodecString { get; private set; }

        public int ViewerCount
        {
            get
            {
                lock (_sync)
                {
                    return _viewers.Count;
                }
            }
        }

        #endregion

        #region Public Methods

        public void Add(VideoFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            Codec = frame.Codec;
            if (frame.Codec != VideoCodec.H264)
            {
                _ready.TrySetResult(false);
                return;
            }

            lock (_sync)
            {
                if (frame.IsKeyFrame && !Start(frame))
                {
                    return;
                }

                if (_init == null)
                {
                    // Joined mid-GOP: wait for a key frame.
                    return;
                }

                if (frame.Timestamp < _last)
                {
                    // A new stream (the camera reconnected): start over at its next key frame.
                    Reset();
                    return;
                }

                var delta = frame.Timestamp - _last;
                if (delta > TimeSpan.Zero && delta < TimeSpan.FromSeconds(1))
                {
                    _duration = (uint)Math.Max(1, Math.Round(delta.TotalSeconds * FragmentedMp4.Timescale));
                }

                _last = frame.Timestamp;
                var decodeTime = (ulong)Math.Round((frame.Timestamp - _first.Value).TotalSeconds * FragmentedMp4.Timescale);
                var fragment = FragmentedMp4.Fragment(++_sequence, decodeTime, _duration, frame.Data, frame.IsKeyFrame);

                if (_gop.Count < _maxCachedFrames)
                {
                    _gop.Add(fragment);
                }

                foreach (var viewer in _viewers.Where(v => !v.Channel.Writer.TryWrite(fragment)).ToList())
                {
                    Drop(viewer);
                }
            }
        }

        /// <summary>Waits until a viewer can start (an H.264 key frame arrived); false on timeout or another codec.</summary>
        public async Task<bool> WaitUntilReadyAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Task<bool> ready;
            lock (_sync)
            {
                if (_init != null)
                {
                    return true;
                }

                ready = _ready.Task;
            }

            try
            {
                return await ready.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        /// <summary>A new viewer: the init segment, the frames since the last key frame, then each new frame. Dispose it to leave.</summary>
        public Viewer Watch()
        {
            var viewer = new Viewer(this, Channel.CreateBounded<byte[]>(new BoundedChannelOptions(_viewerBacklog) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait }));
            lock (_sync)
            {
                if (_init == null)
                {
                    viewer.Channel.Writer.TryComplete();
                    return viewer;
                }

                viewer.Channel.Writer.TryWrite(_init);
                foreach (var fragment in _gop.TakeLast(_viewerBacklog - 1))
                {
                    viewer.Channel.Writer.TryWrite(fragment);
                }

                _viewers.Add(viewer);
            }

            return viewer;
        }

        #endregion

        #region Private Methods

        /// <summary>A key frame: starts a new cached GOP, and a new stream when the parameter sets change.</summary>
        private bool Start(VideoFrame frame)
        {
            byte[] sps = null, pps = null;
            foreach (var nal in AnnexB.Split(frame.Data))
            {
                var type = nal.Span[0] & 0x1F;
                if (type == 7 && sps == null)
                {
                    sps = nal.ToArray();
                }
                else if (type == 8 && pps == null)
                {
                    pps = nal.ToArray();
                }
            }

            sps ??= _sps;
            pps ??= _pps;
            var parsed = sps == null || pps == null ? null : H264Sps.Parse(sps);
            if (parsed == null)
            {
                return _init != null;
            }

            if (_init == null || !sps.AsSpan().SequenceEqual(_sps) || !pps.AsSpan().SequenceEqual(_pps) || frame.Timestamp < _last)
            {
                Reset();
                _sps = sps;
                _pps = pps;
                _init = FragmentedMp4.InitSegment(parsed, sps, pps);
                CodecString = parsed.CodecString;
                _first = frame.Timestamp;
                _last = frame.Timestamp;
                _ready.TrySetResult(true);
            }

            _gop.Clear();
            return true;
        }

        private void Reset()
        {
            foreach (var viewer in _viewers.ToList())
            {
                Drop(viewer);
            }

            _gop.Clear();
            _init = null;
            _first = null;
            _last = TimeSpan.Zero;
            _duration = DefaultDuration;
            if (_ready.Task.IsCompleted)
            {
                _ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        private void Drop(Viewer viewer)
        {
            _viewers.Remove(viewer);
            viewer.Channel.Writer.TryComplete();
        }

        private void Leave(Viewer viewer)
        {
            lock (_sync)
            {
                Drop(viewer);
            }
        }

        #endregion

        /// <summary>One browser watching: read <see cref="Fragments"/> until it completes.</summary>
        public sealed class Viewer : IDisposable
        {
            internal Viewer(Mp4LiveStream stream, Channel<byte[]> channel)
            {
                _stream = stream;
                Channel = channel;
            }

            private readonly Mp4LiveStream _stream;

            internal Channel<byte[]> Channel { get; }

            /// <summary>The init segment first, then fragments; completes when the viewer is dropped or the stream restarts.</summary>
            public ChannelReader<byte[]> Fragments => Channel.Reader;

            public void Dispose() => _stream.Leave(this);
        }
    }
}
