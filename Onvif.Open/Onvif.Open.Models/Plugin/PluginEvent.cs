// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;

namespace Onvif.Open.Core.Plugin
{
    /// <summary>
    /// Something a plugin detected, e.g. type "exam.suspicious-behaviour" or "camera.offline-during-schedule".
    /// The recording covering <see cref="Timestamp"/> is the evidence: the API links each event to its segment.
    /// </summary>
    public sealed class PluginEvent
    {
        #region Constructor

        public PluginEvent(
            string type,
            string cameraId = null,
            string message = null,
            PluginEventSeverity severity = PluginEventSeverity.Information,
            IReadOnlyDictionary<string, string> data = null,
            DateTimeOffset? timestamp = null)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                throw new ArgumentNullException(nameof(type));
            }

            Id = Guid.NewGuid();
            Type = type;
            CameraId = cameraId;
            Message = message;
            Severity = severity;
            Data = data ?? new Dictionary<string, string>();
            Timestamp = timestamp ?? default;
        }

        #endregion

        #region Public Variables

        public Guid Id { get; }

        /// <summary>Name of the plugin that published the event (set by the host).</summary>
        public string Source { get; internal set; }

        public string Type { get; }
        public string CameraId { get; }
        public string Message { get; }
        public PluginEventSeverity Severity { get; }
        public IReadOnlyDictionary<string, string> Data { get; }

        /// <summary>When it happened; the host uses the current time when not given.</summary>
        public DateTimeOffset Timestamp { get; internal set; }

        #endregion
    }
}
