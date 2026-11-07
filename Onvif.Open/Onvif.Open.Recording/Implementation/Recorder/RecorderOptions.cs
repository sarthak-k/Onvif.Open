// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Recording.Implementation.Recorder
{
    internal sealed class RecorderOptions
    {
        #region Public Variables

        public TimeSpan MinReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>A connected stream that delivers no frame for this long is considered dead and reconnected.</summary>
        public TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(15);

        #endregion
    }
}
