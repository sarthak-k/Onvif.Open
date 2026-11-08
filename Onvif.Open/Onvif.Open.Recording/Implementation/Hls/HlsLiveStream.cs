// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Format;

namespace Onvif.Open.Recording.Implementation.Hls
{
    /// <summary>
    /// Live HLS (RFC 8216) for browsers: packs a camera's frames into short MPEG-TS segments in memory, starting
    /// each at a key frame, and keeps a sliding window of them. Feed it with <see cref="Add(VideoFrame)"/> and
    /// <see cref="Add(AudioFrame)"/> (AAC is included); serve <see cref="GetPlaylist"/> and <see cref="TryGetSegment"/>.
    /// </summary>
    public sealed class HlsLiveStream
    {
        #region Constructor

        public HlsLiveStream(TimeSpan? targetDuration = null, int windowSize = 6)
        {
            _targetDuration = targetDuration ?? TimeSpan.FromSeconds(2);

            if (_targetDuration <= TimeSpan.Zero || windowSize < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(targetDuration));
            }

            _windowSize = windowSize;
        }

        #endregion

        #region Private Variables

        private const int MaxSegmentBytes = 64 * 1024 * 1024;

        private readonly TimeSpan _targetDuration;
        private readonly int _windowSize;
        private readonly object _sync = new object();
        private readonly LinkedList<Segment> _segments = new LinkedList<Segment>();
        private TaskCompletionSource<bool> _segmentAdded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private MemoryStream _current;
        private ISegmentMuxer _muxer;
        private VideoCodec _codec;
        private AudioFormat _audio;
        private TimeSpan _currentStart;
        private TimeSpan _lastTimestamp;
        private long _nextSequence;
        private bool _discontinuity;

        #endregion

        #region Public Variables

        /// <summary>Completed segments in the window.</summary>
        public int SegmentCount
        {
            get
            {
                lock (_sync)
                {
                    return _segments.Count;
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

            lock (_sync)
            {
                // A reconnect restarts the timestamps, a codec change needs a new decoder: both start a new segment.
                var restarted = _muxer != null && (frame.Timestamp < _lastTimestamp || frame.Codec != _codec);
                if (restarted)
                {
                    Cut(frame.Timestamp, keepGoing: false);
                    _discontinuity = true;
                }

                if (_muxer == null)
                {
                    if (!frame.IsKeyFrame)
                    {
                        return;
                    }

                    Begin(frame);
                }
                else if (frame.IsKeyFrame && frame.Timestamp - _currentStart >= _targetDuration)
                {
                    Cut(frame.Timestamp, keepGoing: true);
                    Begin(frame);
                }

                _lastTimestamp = frame.Timestamp;
                _muxer.Write(frame);

                if (_current.Length > MaxSegmentBytes)
                {
                    // A camera without key frames for a long time: never grow without bound.
                    _muxer = null;
                    _current = null;
                    _discontinuity = true;
                }
            }
        }

        public void Add(AudioFrame frame)
        {
            if (frame == null || frame.Codec != AudioCodec.Aac)
            {
                return;
            }

            lock (_sync)
            {
                if (_audio == null && SegmentFormats.MpegTs.SupportsAudio(frame.Format))
                {
                    // Takes effect from the next segment, which then carries the audio track.
                    _audio = frame.Format;
                }

                _muxer?.Write(frame);
            }
        }

        /// <summary>The media playlist; <paramref name="segmentUri"/> turns a sequence number into the segment's URI.</summary>
        public string GetPlaylist(Func<long, string> segmentUri)
        {
            lock (_sync)
            {
                var builder = new StringBuilder();
                var target = _segments.Count == 0 ? _targetDuration : _segments.Max(s => s.Duration);

                builder.Append("#EXTM3U\n#EXT-X-VERSION:3\n");
                builder.Append("#EXT-X-TARGETDURATION:").Append((int)Math.Ceiling(target.TotalSeconds)).Append('\n');
                builder.Append("#EXT-X-MEDIA-SEQUENCE:").Append(_segments.First?.Value.Sequence ?? _nextSequence).Append('\n');

                foreach (var segment in _segments)
                {
                    if (segment.Discontinuity)
                    {
                        builder.Append("#EXT-X-DISCONTINUITY\n");
                    }

                    builder.Append("#EXTINF:").Append(segment.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(",\n");
                    builder.Append(segmentUri(segment.Sequence)).Append('\n');
                }

                return builder.ToString();
            }
        }

        public bool TryGetSegment(long sequence, out byte[] data)
        {
            lock (_sync)
            {
                data = _segments.FirstOrDefault(s => s.Sequence == sequence)?.Data;
                return data != null;
            }
        }

        /// <summary>Completes when at least <paramref name="count"/> segments are available (a player needs a few to start).</summary>
        public async Task<bool> WaitForSegmentsAsync(int count, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cancel.CancelAfter(timeout);

                while (true)
                {
                    Task added;
                    lock (_sync)
                    {
                        if (_segments.Count >= Math.Min(count, _windowSize))
                        {
                            return true;
                        }

                        added = _segmentAdded.Task;
                    }

                    try
                    {
                        await added.WaitAsync(cancel.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }
                }
            }
        }

        #endregion

        #region Private Methods

        private void Begin(VideoFrame keyFrame)
        {
            _codec = keyFrame.Codec;
            _current = new MemoryStream();
            _muxer = SegmentFormats.MpegTs.CreateMuxer(_current, _codec, _audio);
            _currentStart = keyFrame.Timestamp;
        }

        private void Cut(TimeSpan end, bool keepGoing)
        {
            if (_muxer == null)
            {
                return;
            }

            _muxer.Dispose();
            var duration = keepGoing ? end - _currentStart : _lastTimestamp - _currentStart;

            if (duration > TimeSpan.Zero)
            {
                _segments.AddLast(new Segment(_nextSequence++, _current.ToArray(), duration, _discontinuity));
                _discontinuity = false;

                while (_segments.Count > _windowSize)
                {
                    _segments.RemoveFirst();
                }

                var added = _segmentAdded;
                _segmentAdded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                added.TrySetResult(true);
            }

            _muxer = null;
            _current = null;
        }

        #endregion

        private sealed class Segment
        {
            public Segment(long sequence, byte[] data, TimeSpan duration, bool discontinuity)
            {
                Sequence = sequence;
                Data = data;
                Duration = duration;
                Discontinuity = discontinuity;
            }

            public long Sequence { get; }
            public byte[] Data { get; }
            public TimeSpan Duration { get; }
            public bool Discontinuity { get; }
        }
    }
}
