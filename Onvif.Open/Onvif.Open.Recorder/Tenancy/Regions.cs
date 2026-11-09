// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Recorder.Tenancy
{
    /// <summary>
    /// Data regions a customer can choose at sign-up; all of an organization's data stays in its region.
    /// </summary>
    public static class Regions
    {
        public const string Asia = "asia";
        public const string UnitedStates = "us";

        public static IReadOnlyList<(string Id, string Name)> All { get; } = new[] { (Asia, "Asia"), (UnitedStates, "United States") };

        public static string Normalize(string region)
        {
            return All.Select(r => r.Id).FirstOrDefault(id => string.Equals(id, region?.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>How a recording policy records.</summary>
    public static class RecordingModes
    {
        /// <summary>Records all the time.</summary>
        public const string Always = "Always";

        /// <summary>Records around events and detections only (with pre- and post-roll).</summary>
        public const string EventsOnly = "EventsOnly";

        /// <summary>Records all the time during the policy's hours, around events otherwise.</summary>
        public const string Schedule = "Schedule";

        public static IReadOnlyList<string> All { get; } = new[] { Always, EventsOnly, Schedule };

        public static string Normalize(string mode)
        {
            return All.FirstOrDefault(m => string.Equals(m, mode?.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
