// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Alerts
{
    public static class AlertKinds
    {
        /// <summary>An event of chosen categories starts.</summary>
        public const string Event = "Event";

        /// <summary>A camera has been offline for a number of minutes.</summary>
        public const string Offline = "Offline";

        public static string Normalize(string kind) =>
            string.Equals(kind, Event, StringComparison.OrdinalIgnoreCase) ? Event
            : string.Equals(kind, Offline, StringComparison.OrdinalIgnoreCase) ? Offline
            : null;
    }

    public static class Severities
    {
        public const string Info = "Info";
        public const string Warning = "Warning";
        public const string Critical = "Critical";

        public static readonly string[] All = { Info, Warning, Critical };

        /// <summary>0 for Info (or unknown), 1 for Warning, 2 for Critical.</summary>
        public static int Rank(string severity) => Math.Max(0, Array.FindIndex(All, s => string.Equals(s, severity, StringComparison.OrdinalIgnoreCase)));

        public static string Normalize(string severity) => All.FirstOrDefault(s => string.Equals(s, severity?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// When to raise an alert and whom to tell: an event of some categories, or a camera offline for some minutes, at
    /// some cameras or sites (none: all), optionally only during set hours in the site's time zone. Each camera alerts
    /// at most once per <see cref="CooldownMinutes"/> for a rule.
    /// </summary>
    public sealed class AlertRule
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; } = true;

        /// <summary>See <see cref="AlertKinds"/>.</summary>
        public string Kind { get; set; } = AlertKinds.Event;

        /// <summary>Event rules: the categories that alert (see Events.EventCategories); none: every category.</summary>
        public List<string> Categories { get; set; } = new List<string>();

        /// <summary>Event rules: the least severe event that alerts; null: any.</summary>
        public string MinSeverity { get; set; }

        /// <summary>Offline rules: how long a camera is offline before it alerts.</summary>
        public int OfflineMinutes { get; set; } = 5;

        /// <summary>None: every site.</summary>
        public List<string> SiteIds { get; set; } = new List<string>();

        /// <summary>None: every camera (of the sites).</summary>
        public List<string> CameraIds { get; set; } = new List<string>();

        /// <summary>When it alerts, in the site's time zone; none: at any time.</summary>
        public List<ScheduleWindow> Hours { get; set; } = new List<ScheduleWindow>();

        public int CooldownMinutes { get; set; } = 5;

        /// <summary>Who is emailed.</summary>
        public List<string> Emails { get; set; } = new List<string>();

        /// <summary>Where alerts are posted (JSON, signed when there is a secret).</summary>
        public string WebhookUrl { get; set; }

        [JsonIgnore]
        public string WebhookSecret { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        [JsonIgnore]
        public RecordingSchedule Schedule
        {
            get
            {
                var cached = _schedule;
                if (cached.For != Hours)
                {
                    cached = (Hours, RecordingSchedule.From(Hours));
                    _schedule = cached;
                }

                return cached.Schedule;
            }
        }

        private (List<ScheduleWindow> For, RecordingSchedule Schedule) _schedule;
    }

    /// <summary>
    /// Whether a rule catches something. Pure: the caller gives the time in the site's time zone.
    /// </summary>
    public static class AlertMatcher
    {
        /// <summary>Whether the rule watches this camera (or, for events about no camera, this site).</summary>
        public static bool Watches(AlertRule rule, string organizationId, string siteId, string cameraId)
        {
            if (rule == null || !rule.Enabled || !string.Equals(rule.OrganizationId, organizationId, StringComparison.Ordinal))
            {
                return false;
            }

            if (rule.SiteIds.Count > 0 && (siteId == null || !rule.SiteIds.Contains(siteId, StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }

            return rule.CameraIds.Count == 0 || (cameraId != null && rule.CameraIds.Contains(cameraId, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Whether the rule is on at <paramref name="siteTime"/> (its hours, if it has any).</summary>
        public static bool IsOnAt(AlertRule rule, DateTime siteTime) => rule.Hours.Count == 0 || rule.Schedule?.IsOnAt(siteTime) != false;

        /// <summary>Whether an event rule alerts for <paramref name="record"/>, which started at <paramref name="siteTime"/> in its site's time.</summary>
        public static bool Matches(AlertRule rule, EventRecord record, DateTime siteTime)
        {
            return rule?.Kind == AlertKinds.Event
                && record != null
                && Watches(rule, record.OrganizationId, record.SiteId, record.CameraId)
                && (rule.Categories.Count == 0 || rule.Categories.Contains(record.Category, StringComparer.OrdinalIgnoreCase))
                && Severities.Rank(record.Severity) >= Severities.Rank(rule.MinSeverity)
                && IsOnAt(rule, siteTime);
        }

        /// <summary>Whether an offline rule alerts for <paramref name="camera"/>, offline for <paramref name="offlineFor"/>.</summary>
        public static bool MatchesOffline(AlertRule rule, CameraEntry camera, TimeSpan offlineFor, DateTime siteTime)
        {
            return rule?.Kind == AlertKinds.Offline
                && camera != null
                && Watches(rule, camera.OrganizationId, camera.SiteId, camera.Id)
                && offlineFor >= TimeSpan.FromMinutes(rule.OfflineMinutes)
                && IsOnAt(rule, siteTime);
        }
    }
}
