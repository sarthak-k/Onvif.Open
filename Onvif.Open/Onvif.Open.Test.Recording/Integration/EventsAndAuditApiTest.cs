// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Stored events (what cameras, detections, recording and plugins report) and the audit log.
    /// </summary>
    public sealed class EventsAndAuditApiTest : IDisposable
    {
        #region Constructor

        public EventsAndAuditApiTest()
        {
            _storage = new TempDirectory();
            _settings = new TestSettings();
            _lyon = _settings.AddSite("Lyon");
            _settings.Camera(new CameraOptions { Id = "lobby", Name = "Lobby", Enabled = false, RtspUrl = "rtsp://127.0.0.1:1/lobby" })
                     .Camera(new CameraOptions { Id = "dock", Name = "Dock", SiteId = _lyon, Enabled = false, RtspUrl = "rtsp://127.0.0.1:1/dock" })
                     .Camera(new CameraOptions { Id = "hall", Name = "Hall", RtspUrl = "rtsp://127.0.0.1:1/hall" });

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Recording:Retention:MinFreeSpaceGB", "0");
            });
            _owner = TestSettings.Authenticate(_factory.CreateClient());
        }

        #endregion

        #region Private Variables

        private readonly TempDirectory _storage;
        private readonly TestSettings _settings;
        private readonly string _lyon;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _owner;

        #endregion

        #region Private Methods

        private EventRecorder Recorder => _factory.Services.GetServices<IHostedService>().OfType<EventRecorder>().Single();

        private static async Task<List<JsonElement>> ListAsync(HttpClient client, string uri) =>
            JsonDocument.Parse(await client.GetStringAsync(uri)).RootElement.EnumerateArray().ToList();

        /// <summary>Waits until the background writers have stored what the condition looks for.</summary>
        private static async Task<List<JsonElement>> EventuallyAsync(HttpClient client, string uri, Func<List<JsonElement>, bool> condition)
        {
            List<JsonElement> items = null;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                items = await ListAsync(client, uri);
                if (condition(items))
                {
                    return items;
                }

                await Task.Delay(100);
            }

            Assert.Fail($"{uri} never matched; last answer: {string.Join(", ", items?.Select(i => i.ToString()) ?? Array.Empty<string>())}");
            return null;
        }

        private static CameraEvent Motion(string camera, DateTimeOffset time, bool state) =>
            new CameraEvent(camera, "tns1:RuleEngine/CellMotionDetector/Motion", time, "Changed", null, null, new Dictionary<string, string> { ["IsMotion"] = state ? "true" : "false" });

        private static AnalyticsFrame Seen(string camera, DateTimeOffset time, string objectClass, double likelihood) =>
            new AnalyticsFrame(time, "VideoAnalytics", new[] { new DetectedObject("1", new[] { new ObjectClassification(objectClass, likelihood) }, null) }, camera);

        private static string Text(JsonElement element, string property) => element.GetProperty(property).ValueKind == JsonValueKind.Null ? null : element.GetProperty(property).GetString();

        #endregion

        #region Public Methods

        [Fact]
        public async Task WhatCamerasReportShouldBeStoredAsEvents()
        {
            var t = DateTimeOffset.UtcNow.AddMinutes(-10);
            var recorder = Recorder;
            await Task.Delay(200);

            // Motion on, then off: one event with its duration.
            recorder.OnCameraEvent(this, Motion("lobby", t, true));
            recorder.OnCameraEvent(this, Motion("lobby", t.AddSeconds(12), false));

            // A person seen in several frames (and a low-likelihood guess that is ignored): one event until the last sighting,
            // ended once nobody has been seen for a while.
            var seen = DateTimeOffset.UtcNow.AddSeconds(-3);
            recorder.OnAnalytics(this, Seen("lobby", seen, "Human", 0.9));
            recorder.OnAnalytics(this, Seen("lobby", seen.AddSeconds(2), "Human", 0.8));
            recorder.OnAnalytics(this, Seen("lobby", seen.AddSeconds(2), "Vehicle", 0.2));

            // The camera went offline and came back.
            recorder.OnStateChanged(this, new CameraStateChange("lobby", RecordingState.Recording, RecordingState.Reconnecting, "Connection refused", t.AddSeconds(30)));
            recorder.OnStateChanged(this, new CameraStateChange("lobby", RecordingState.Reconnecting, RecordingState.Recording, null, t.AddSeconds(90)));

            // A plugin's finding.
            recorder.OnPluginEvent(this, new PluginEvent("exam.suspicious", "lobby", "Two faces at one desk", PluginEventSeverity.Critical, new Dictionary<string, string> { ["confidence"] = "0.91" }, t.AddSeconds(40)));

            var events = await EventuallyAsync(_owner, "/api/events?cameraId=lobby", list => list.Count == 4 && list.All(e => Text(e, "endTime") != null));

            Assert.Equal(new[] { "person", "plugin", "offline", "motion" }, events.Select(e => Text(e, "category")));
            var motion = events[3];
            Assert.Equal(("Motion", "Lobby", TestSettings.SiteId), (Text(motion, "title"), Text(motion, "cameraName"), Text(motion, "siteId")));
            Assert.Equal(12, (motion.GetProperty("endTime").GetDateTimeOffset() - motion.GetProperty("startTime").GetDateTimeOffset()).TotalSeconds, 3);

            var person = events[0];
            Assert.Equal(2, (person.GetProperty("endTime").GetDateTimeOffset() - person.GetProperty("startTime").GetDateTimeOffset()).TotalSeconds, 3);
            Assert.Equal("Human", person.GetProperty("data").GetProperty("classes")[0].GetString());

            Assert.Equal(("Warning", "Camera offline"), (Text(events[2], "severity"), Text(events[2], "title")));
            Assert.Equal(("Critical", "Two faces at one desk"), (Text(events[1], "severity"), Text(events[1], "title")));

            Assert.Equal(new[] { "person" }, (await ListAsync(_owner, "/api/events?category=person")).Select(e => Text(e, "category")));
            Assert.Empty(await ListAsync(_owner, $"/api/events?to={Uri.EscapeDataString(t.AddSeconds(-1).ToString("O"))}"));
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.GetAsync("/api/events?category=nonsense")).StatusCode);
        }

        [Fact]
        public async Task EventsOfOtherSitesShouldStayHidden()
        {
            var t = DateTimeOffset.UtcNow.AddMinutes(-5);
            var recorder = Recorder;
            await Task.Delay(200);
            recorder.OnCameraEvent(this, Motion("lobby", t, true));
            recorder.OnCameraEvent(this, Motion("dock", t, true));
            await EventuallyAsync(_owner, "/api/events?category=motion", list => list.Count == 2);

            var created = await _owner.PostAsJsonAsync("/api/users", new { userName = "lyon", password = "Temporary-Pass-1", role = "Viewer", sites = new[] { _lyon } });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            using (var viewer = _factory.CreateClient())
            {
                var first = await TestSettings.SignInAsync(viewer, "lyon", "Temporary-Pass-1");
                viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
                var changed = await viewer.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Temporary-Pass-1", newPassword = "Lyon-Own-Pass-2" });
                viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", JsonDocument.Parse(await changed.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString());

                Assert.Equal(new[] { "dock" }, (await ListAsync(viewer, "/api/events?category=motion")).Select(e => Text(e, "cameraId")));
                Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/events?cameraId=lobby")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/audit")).StatusCode);
            }
        }

        [Fact]
        public async Task ChangesSignInsAndViewingShouldBeAudited()
        {
            using (var anonymous = _factory.CreateClient())
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "wrong-password" })).StatusCode);
            }

            var site = await _owner.PostAsJsonAsync("/api/sites", new { name = "Airport", timeZone = "UTC" });
            var siteId = JsonDocument.Parse(await site.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
            Assert.Equal(HttpStatusCode.NoContent, (await _owner.DeleteAsync($"/api/sites/{siteId}")).StatusCode);

            // A refused change is not an action that happened.
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.PostAsJsonAsync("/api/sites", new { name = "" })).StatusCode);

            // Watching recordings twice within ten minutes: one entry.
            Assert.Equal(HttpStatusCode.OK, (await _owner.GetAsync("/api/cameras/hall/recordings.m3u8")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _owner.GetAsync("/api/cameras/hall/recordings.m3u8")).StatusCode);

            var entries = await EventuallyAsync(_owner, "/api/audit", list => list.Count >= 5);
            var actions = entries.Select(e => Text(e, "action")).Reverse().ToList();
            Assert.Equal(new[] { "auth.signin", "auth.signin_failed", "site.create", "site.delete", "video.playback" }, actions);

            var deleted = entries.Single(e => Text(e, "action") == "site.delete");
            Assert.Equal(("Airport", siteId, "admin"), (Text(deleted, "targetName"), Text(deleted, "targetId"), Text(deleted, "userName")));
            Assert.Equal("Hall", Text(entries.Single(e => Text(e, "action") == "video.playback"), "targetName"));
            Assert.Equal("admin", Text(entries.Single(e => Text(e, "action") == "auth.signin_failed"), "userName"));

            Assert.Equal(new[] { "site.delete", "site.create" }, (await ListAsync(_owner, "/api/audit?action=site")).Select(e => Text(e, "action")));
        }

        public void Dispose()
        {
            _owner.Dispose();
            _factory.Dispose();
            _storage.Dispose();
            _settings.Dispose();
        }

        #endregion
    }
}
