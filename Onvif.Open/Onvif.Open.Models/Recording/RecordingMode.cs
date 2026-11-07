// Sarthak Killedar
// 2026-11-07

namespace Onvif.Open.Core.Recording
{
    public enum RecordingMode
    {
        /// <summary>Record all the time.</summary>
        Continuous = 0,

        /// <summary>
        /// Keep the stream connected but only record while triggered (camera events, plugins, the API),
        /// including a pre-roll before and a post-roll after the trigger.
        /// </summary>
        Triggered = 1
    }
}
