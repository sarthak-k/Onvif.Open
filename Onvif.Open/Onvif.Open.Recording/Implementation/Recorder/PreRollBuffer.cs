// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Recording.Implementation.Recorder
{
    /// <summary>
    /// Keeps the most recent whole GOPs covering at least the pre-roll, so a triggered recording can start
    /// before the trigger and still begin with a key frame.
    /// </summary>
    internal sealed class PreRollBuffer
    {
        #region Constructor

        public PreRollBuffer(TimeSpan preRoll, long maxBytes = 64 * 1024 * 1024)
        {
            _preRoll = preRoll < TimeSpan.Zero ? TimeSpan.Zero : preRoll;
            _maxBytes = maxBytes;
        }

        #endregion

        #region Private Variables

        private readonly TimeSpan _preRoll;
        private readonly long _maxBytes;
        private readonly LinkedList<List<VideoFrame>> _gops = new LinkedList<List<VideoFrame>>();
        private long _bytes;

        #endregion

        #region Public Variables

        public int FrameCount
        {
            get
            {
                var count = 0;
                foreach (var gop in _gops)
                {
                    count += gop.Count;
                }

                return count;
            }
        }

        #endregion

        #region Public Methods

        public void Add(VideoFrame frame)
        {
            if (frame.IsKeyFrame)
            {
                _gops.AddLast(new List<VideoFrame>());
            }
            else if (_gops.Count == 0)
            {
                // Nothing decodable before the first key frame.
                return;
            }

            _gops.Last.Value.Add(frame);
            _bytes += frame.Data.Length;
            Trim(frame.Timestamp);
        }

        /// <summary>Removes and returns the buffered frames, oldest first (starting with a key frame when any).</summary>
        public IReadOnlyList<VideoFrame> Drain()
        {
            var frames = new List<VideoFrame>();

            foreach (var gop in _gops)
            {
                frames.AddRange(gop);
            }

            _gops.Clear();
            _bytes = 0;
            return frames;
        }

        public void Clear()
        {
            _gops.Clear();
            _bytes = 0;
        }

        #endregion

        #region Private Methods

        private void Trim(TimeSpan newest)
        {
            // Drop the oldest GOP while the remaining ones still cover the pre-roll (or memory is exceeded).
            while (_gops.Count > 1)
            {
                var second = _gops.First.Next.Value[0].Timestamp;
                if (newest - second < _preRoll && _bytes <= _maxBytes)
                {
                    break;
                }

                foreach (var frame in _gops.First.Value)
                {
                    _bytes -= frame.Data.Length;
                }

                _gops.RemoveFirst();
            }
        }

        #endregion
    }
}
