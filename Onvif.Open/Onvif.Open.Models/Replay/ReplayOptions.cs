// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Replay
{
    /// <summary>
    /// How a recording stored on the device is played back (ONVIF Profile G replay).
    /// </summary>
    public sealed class ReplayOptions
    {
        #region Public Variables

        /// <summary>Stop when this recording time is reached. Default: play until the end of the recording.</summary>
        public DateTimeOffset? To { get; set; }

        /// <summary>
        /// True (default): the device sends the recording at <see cref="Speed"/> times real time, for viewing.
        /// False: as fast as the device and network allow, e.g. to export a clip.
        /// </summary>
        public bool RealTime { get; set; } = true;

        /// <summary>Playback speed when <see cref="RealTime"/> is true, e.g. 2 for double speed. Default: 1.</summary>
        public double Speed { get; set; } = 1.0;

        #endregion

        #region Public Methods

        /// <summary>Throws for invalid options; returns them for chaining.</summary>
        public ReplayOptions Validate(DateTimeOffset from)
        {
            if (To.HasValue && To.Value <= from)
            {
                throw new ArgumentOutOfRangeException(nameof(To), "The end of the replay must be after its start.");
            }

            if (double.IsNaN(Speed) || Speed <= 0 || Speed > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(Speed), "Speed must be greater than 0 and at most 64.");
            }

            return this;
        }

        #endregion
    }
}
