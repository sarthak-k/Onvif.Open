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
        /// Receive the camera's audio: AAC is recorded with the video (MPEG-TS), every supported codec (also G.711) is
        /// raised as IRecordingManager.AudioReceived and passed to plugins. Needs <see cref="StreamTransport.Rtsp"/>. Default: off.
        /// </summary>
        public bool Audio { get; set; }

        /// <summary>
        /// Event topics (prefixes ignored, sub-topics included) that trigger recording, e.g.
        /// "RuleEngine/CellMotionDetector/Motion". Stateful events trigger while their state is true.
        /// </summary>
        public IList<string> EventTriggers { get; } = new List<string>();

        /// <summary>
        /// Receive the camera's analytics metadata (ONVIF Profile M: detected objects) and raise it as
        /// IRecordingManager.AnalyticsReceived (and to plugins). Implied by <see cref="AnalyticsTriggers"/>. Default: off.
        /// </summary>
        public bool ReceiveAnalytics { get; set; }

        /// <summary>
        /// Object classes that trigger recording while the camera sees them, e.g. "Human" or "Vehicle" (case-insensitive,
        /// as the camera names them).
        /// </summary>
        public IList<string> AnalyticsTriggers { get; } = new List<string>();

        /// <summary>Minimum likelihood (0..1) for <see cref="AnalyticsTriggers"/>. Default: 0.5.</summary>
        public double AnalyticsMinLikelihood { get; set; } = 0.5;

        /// <summary>Media profile with the metadata configuration; the first one that has one when null.</summary>
        public string AnalyticsProfileToken { get; set; }

        /// <summary>True when analytics are received (explicitly or because of triggers).</summary>
        public bool UsesAnalytics => ReceiveAnalytics || AnalyticsTriggers.Count > 0;

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
