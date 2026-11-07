// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Streaming.Implementation.Depacketizer
{
    /// <summary>
    /// Converts 32 bit RTP timestamps into a monotonic time relative to the first packet, handling wrap-around.
    /// </summary>
    internal sealed class RtpTimestampClock
    {
        #region Constructor

        public RtpTimestampClock(int clockRate)
        {
            _clockRate = clockRate > 0 ? clockRate : 90000;
        }

        #endregion

        #region Private Variables

        private readonly int _clockRate;
        private bool _started;
        private uint _last;
        private long _elapsedTicks;

        #endregion

        #region Public Methods

        public TimeSpan ToTime(uint rtpTimestamp)
        {
            if (!_started)
            {
                _started = true;
                _last = rtpTimestamp;
                return TimeSpan.Zero;
            }

            // Signed difference copes with both wrap-around and slightly re-ordered timestamps.
            _elapsedTicks += unchecked((int)(rtpTimestamp - _last));
            _last = rtpTimestamp;

            return TimeSpan.FromTicks(_elapsedTicks * TimeSpan.TicksPerSecond / _clockRate);
        }

        #endregion
    }
}
