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
        public PluginEventSeverity? MinimumSeverity { get; set; }
        public int Limit { get; set; } = 100;

        #endregion
    }
}
