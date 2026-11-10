// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Text;
using Onvif.Open.Recorder.Alerts;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public sealed class AlertMatcherTest
    {
        private static AlertRule Rule(Action<AlertRule> change = null)
        {
            var rule = new AlertRule { Id = "r", OrganizationId = "org", Name = "Rule" };
            change?.Invoke(rule);
            return rule;
        }

        private static EventRecord Event(string category = "person", string severity = "Info", string camera = "cam", string site = "site") =>
            new EventRecord { OrganizationId = "org", SiteId = site, CameraId = camera, Category = category, Severity = severity };

        private static readonly DateTime Noon = new DateTime(2026, 11, 9, 12, 0, 0); // a Monday

        [Fact]
        public void ARuleShouldMatchItsCategoriesCamerasSitesAndSeverity()
        {
            Assert.True(AlertMatcher.Matches(Rule(), Event(), Noon));
            Assert.False(AlertMatcher.Matches(Rule(r => r.Enabled = false), Event(), Noon));
            Assert.False(AlertMatcher.Matches(Rule(r => r.OrganizationId = "other"), Event(), Noon));

            Assert.True(AlertMatcher.Matches(Rule(r => r.Categories = new List<string> { "person" }), Event(), Noon));
            Assert.False(AlertMatcher.Matches(Rule(r => r.Categories = new List<string> { "vehicle" }), Event(), Noon));

            Assert.False(AlertMatcher.Matches(Rule(r => r.SiteIds = new List<string> { "elsewhere" }), Event(), Noon));
            Assert.False(AlertMatcher.Matches(Rule(r => r.SiteIds = new List<string> { "site" }), Event(site: null, camera: null), Noon));
            Assert.True(AlertMatcher.Matches(Rule(r => r.CameraIds = new List<string> { "CAM" }), Event(), Noon));
            Assert.False(AlertMatcher.Matches(Rule(r => r.CameraIds = new List<string> { "other" }), Event(), Noon));

            Assert.True(AlertMatcher.Matches(Rule(r => r.MinSeverity = "Warning"), Event(severity: "Critical"), Noon));
            Assert.False(AlertMatcher.Matches(Rule(r => r.MinSeverity = "Warning"), Event(severity: "Info"), Noon));

            Assert.False(AlertMatcher.Matches(Rule(r => r.Kind = AlertKinds.Offline), Event(), Noon));
        }

        [Fact]
        public void HoursShouldBeInTheSiteTimeAndMayRunPastMidnight()
        {
            var nights = Rule(r => r.Hours = new List<ScheduleWindow> { new ScheduleWindow { Days = { DayOfWeek.Monday }, Start = "22:00", End = "06:00" } });

            Assert.False(AlertMatcher.Matches(nights, Event(), Noon));
            Assert.True(AlertMatcher.Matches(nights, Event(), Noon.AddHours(11)));
            Assert.True(AlertMatcher.Matches(nights, Event(), Noon.AddHours(17))); // Tuesday 05:00
            Assert.False(AlertMatcher.Matches(nights, Event(), Noon.AddHours(18)));

            // Changing the hours takes effect at once.
            nights.Hours = new List<ScheduleWindow> { new ScheduleWindow { Days = { DayOfWeek.Monday }, Start = "11:00", End = "13:00" } };
            Assert.True(AlertMatcher.Matches(nights, Event(), Noon));
        }

        [Fact]
        public void OfflineRulesShouldWaitTheirMinutes()
        {
            var rule = Rule(r => { r.Kind = AlertKinds.Offline; r.OfflineMinutes = 5; });
            var camera = new CameraEntry("cam", "org", "site", "p", "Cam", false, TimeSpan.FromDays(7), TimeSpan.FromDays(30));

            Assert.False(AlertMatcher.MatchesOffline(rule, camera, TimeSpan.FromMinutes(4), Noon));
            Assert.True(AlertMatcher.MatchesOffline(rule, camera, TimeSpan.FromMinutes(5), Noon));
            Assert.False(AlertMatcher.MatchesOffline(Rule(), camera, TimeSpan.FromHours(1), Noon));
        }

        [Fact]
        public void WebhooksShouldBeSignedWithHmacSha256OfTheBody()
        {
            // echo -n '{"a":1}' | openssl dgst -sha256 -hmac key
            Assert.Equal("sha256=88a67f24bbcdaed0e6c997404bb79a743baf44c6bab2f4c27328e3009d22e342", WebhookNotifier.Sign("key", Encoding.UTF8.GetBytes("{\"a\":1}")));
        }
    }
}
