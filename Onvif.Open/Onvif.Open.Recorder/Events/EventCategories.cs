// Sarthak Killedar
// 2026-11-09

using System;

namespace Onvif.Open.Recorder.Events
{
    /// <summary>
    /// What people search for: a handful of categories instead of hundreds of ONVIF topics and object classes.
    /// </summary>
    public static class EventCategories
    {
        public const string Motion = "motion";
        public const string Person = "person";
        public const string Vehicle = "vehicle";
        public const string Object = "object";
        public const string Intrusion = "intrusion";
        public const string Tamper = "tamper";
        public const string Input = "input";
        public const string Offline = "offline";
        public const string Plugin = "plugin";
        public const string Other = "other";

        public static readonly string[] All = { Motion, Person, Vehicle, Object, Intrusion, Tamper, Input, Offline, Plugin, Other };

        /// <summary>The category of an ONVIF event topic (namespace prefixes removed), e.g. "RuleEngine/CellMotionDetector/Motion".</summary>
        public static string OfTopic(string topicPath)
        {
            var topic = topicPath ?? string.Empty;
            bool Has(string part) => topic.Contains(part, StringComparison.OrdinalIgnoreCase);

            if (Has("Tamper") || Has("GlobalSceneChange") || Has("ImageTooBlurry") || Has("ImageTooDark") || Has("ImageTooBright") || Has("SignalLoss"))
            {
                return Tamper;
            }

            if (Has("LineDetector") || Has("FieldDetector") || Has("Intrusion") || Has("LineCross") || Has("Loitering"))
            {
                return Intrusion;
            }

            if (Has("Motion"))
            {
                return Motion;
            }

            if (Has("DigitalInput") || Has("Trigger/Relay") || Has("IO/") || Has("AudioAnalytics"))
            {
                return Input;
            }

            return Other;
        }

        /// <summary>The category of a detected object class, e.g. "Human", "Vehicle", "Car".</summary>
        public static string OfObjectClass(string objectClass)
        {
            switch ((objectClass ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "human":
                case "person":
                case "face":
                case "humanface":
                case "humanbody":
                    return Person;
                case "vehicle":
                case "car":
                case "truck":
                case "bus":
                case "motorcycle":
                case "bike":
                case "bicycle":
                case "licenseplate":
                    return Vehicle;
                default:
                    return Object;
            }
        }

        /// <summary>A readable title from a topic: its last part, with words separated ("CellMotionDetector/Motion" -> "Motion").</summary>
        public static string TitleOfTopic(string topicPath)
        {
            var last = (topicPath ?? string.Empty).TrimEnd('/');
            var slash = last.LastIndexOf('/');
            last = slash >= 0 ? last.Substring(slash + 1) : last;
            if (last.Length == 0)
            {
                return "Event";
            }

            var builder = new System.Text.StringBuilder(last.Length + 8);
            for (var i = 0; i < last.Length; i++)
            {
                if (i > 0 && char.IsUpper(last[i]) && char.IsLower(last[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(i == 0 ? char.ToUpperInvariant(last[i]) : last[i]);
            }

            return builder.ToString();
        }
    }
}
