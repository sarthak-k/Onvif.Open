// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Core.Events
{
    /// <summary>
    /// A notification from the device's ONVIF Events service, e.g. motion, tampering or a digital input.
    /// </summary>
    public sealed class CameraEvent
    {
        #region Constructor

        public CameraEvent(
            string cameraId,
            string topic,
            DateTimeOffset utcTime,
            string propertyOperation,
            IReadOnlyDictionary<string, string> source,
            IReadOnlyDictionary<string, string> key,
            IReadOnlyDictionary<string, string> data)
        {
            CameraId = cameraId;
            Topic = topic ?? string.Empty;
            TopicPath = NormalizeTopic(Topic);
            UtcTime = utcTime;
            PropertyOperation = propertyOperation;
            Source = source ?? Empty;
            Key = key ?? Empty;
            Data = data ?? Empty;
        }

        #endregion

        #region Private Variables

        private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

        #endregion

        #region Public Variables

        public string CameraId { get; }

        /// <summary>Topic as sent by the device, e.g. "tns1:RuleEngine/CellMotionDetector/Motion".</summary>
        public string Topic { get; }

        /// <summary>
        /// Topic without namespace prefixes, e.g. "RuleEngine/CellMotionDetector/Motion": compare against this,
        /// since devices use different prefixes for the same topic.
        /// </summary>
        public string TopicPath { get; }

        public DateTimeOffset UtcTime { get; }

        /// <summary>"Initialized" (current state when subscribing), "Changed" or "Deleted"; null for plain events.</summary>
        public string PropertyOperation { get; }

        /// <summary>What produced the event, e.g. VideoSourceConfigurationToken, InputToken.</summary>
        public IReadOnlyDictionary<string, string> Source { get; }

        public IReadOnlyDictionary<string, string> Key { get; }

        /// <summary>The event values, e.g. IsMotion = "true", State = "false".</summary>
        public IReadOnlyDictionary<string, string> Data { get; }

        /// <summary>True when the device reports the current state on subscribing rather than a change.</summary>
        public bool IsInitialState => string.Equals(PropertyOperation, "Initialized", StringComparison.OrdinalIgnoreCase);

        #endregion

        #region Public Methods

        /// <summary>
        /// The boolean state of the event, from the first data item that is "true"/"false" (IsMotion, State,
        /// IsTamper, LogicalState, ...); null when the event carries none.
        /// </summary>
        public bool? GetState()
        {
            foreach (var value in Data.Values)
            {
                if (bool.TryParse(value, out var state))
                {
                    return state;
                }

                if (value == "1")
                {
                    return true;
                }

                if (value == "0")
                {
                    return false;
                }
            }

            return null;
        }

        /// <summary>True when <see cref="TopicPath"/> equals <paramref name="topicPath"/> or is below it (prefixes ignored, case-insensitive).</summary>
        public bool MatchesTopic(string topicPath)
        {
            var expected = NormalizeTopic(topicPath ?? string.Empty);
            return expected.Length > 0 &&
                   (string.Equals(TopicPath, expected, StringComparison.OrdinalIgnoreCase) ||
                    TopicPath.StartsWith(expected + "/", StringComparison.OrdinalIgnoreCase));
        }

        public override string ToString()
        {
            return $"{TopicPath} {string.Join(", ", Data.Select(d => d.Key + "=" + d.Value))}".Trim();
        }

        /// <summary>A copy attributed to <paramref name="cameraId"/>.</summary>
        public CameraEvent WithCameraId(string cameraId)
        {
            return new CameraEvent(cameraId, Topic, UtcTime, PropertyOperation, Source, Key, Data);
        }

        internal static string NormalizeTopic(string topic)
        {
            return string.Join("/", topic
                .Trim()
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Contains(':') ? part.Substring(part.IndexOf(':') + 1) : part));
        }

        #endregion
    }
}
