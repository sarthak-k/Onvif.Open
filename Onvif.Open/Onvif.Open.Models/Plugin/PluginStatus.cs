// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;

namespace Onvif.Open.Core.Plugin
{
    public sealed class PluginStatus
    {
        #region Public Variables

        public string Name { get; internal set; }
        public PluginState State { get; internal set; }

        /// <summary>Camera ids the plugin may use; null means every camera.</summary>
        public IReadOnlyList<string> Cameras { get; internal set; }

        /// <summary>Implemented capability interfaces, e.g. "IFrameProcessor".</summary>
        public IReadOnlyList<string> Capabilities { get; internal set; }

        public long FramesDelivered { get; internal set; }

        /// <summary>Frames discarded because the plugin could not keep up.</summary>
        public long FramesDropped { get; internal set; }

        public long NotificationsDelivered { get; internal set; }
        public long NotificationsDropped { get; internal set; }
        public long EventsPublished { get; internal set; }

        /// <summary>Exceptions thrown by the plugin's callbacks.</summary>
        public long Errors { get; internal set; }

        public string LastError { get; internal set; }
        public DateTimeOffset? LastErrorAt { get; internal set; }

        #endregion
    }
}
