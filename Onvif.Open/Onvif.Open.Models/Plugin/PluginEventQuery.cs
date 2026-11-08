// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Plugin
{
    public sealed class PluginEventQuery
    {
        #region Public Variables

        public string CameraId { get; set; }
        public string Type { get; set; }
        public string Source { get; set; }
        public DateTimeOffset? Since { get; set; }

        /// <summary>Only events before this time (exclusive): with <see cref="Since"/>, a time window.</summary>
        public DateTimeOffset? Until { get; set; }
        public PluginEventSeverity? MinimumSeverity { get; set; }
        public int Limit { get; set; } = 100;

        #endregion
    }
}
