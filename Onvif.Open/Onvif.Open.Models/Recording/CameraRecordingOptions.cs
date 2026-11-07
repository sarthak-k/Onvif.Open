// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Core.Recording
{
    /// <summary>
    /// How one camera is recorded. Every property has a sensible default.
    /// </summary>
    public sealed class CameraRecordingOptions
    {
        #region Public Variables

        /// <summary>ONVIF media profile; the first profile when null.</summary>
        public string ProfileToken { get; set; }

        public StreamTransport Transport { get; set; } = StreamTransport.Rtsp;

        /// <summary>Overrides the manager's rotation policy for this camera.</summary>
        public IRotationPolicy RotationPolicy { get; set; }

        public RecordingMode Mode { get; set; } = RecordingMode.Continuous;

        /// <summary>Triggered mode: video kept from before the trigger (rounded to whole GOPs). Default 5 seconds.</summary>
        public TimeSpan PreRoll { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Triggered mode: recording continues this long after the trigger ends. Default 10 seconds.</summary>
        public TimeSpan PostRoll { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Subscribe to the camera's ONVIF events (ONVIF cameras only). Default: on.</summary>
        public bool SubscribeToEvents { get; set; } = true;

        /// <summary>
        /// Event topics (prefixes ignored, sub-topics included) that trigger recording, e.g.
        /// "RuleEngine/CellMotionDetector/Motion". Stateful events trigger while their state is true.
        /// </summary>
        public IList<string> EventTriggers { get; } = new List<string>();

        /// <summary>Motion topics used by most devices.</summary>
        public static IReadOnlyList<string> CommonMotionTopics { get; } = new[]
        {
            "RuleEngine/CellMotionDetector/Motion",
            "RuleEngine/MotionRegionDetector/Motion",
            "VideoSource/MotionAlarm"
        };

        #endregion
    }
}
