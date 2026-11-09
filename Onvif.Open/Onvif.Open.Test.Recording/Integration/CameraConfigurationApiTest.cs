// Sarthak Killedar
// 2026-11-08

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Cameras added, changed and removed through the API while the recorder runs, kept in the settings database.
    /// </summary>
    public sealed class CameraConfigurationApiTest : IDisposable
    {
        #region Constructor

        public CameraConfigurationApiTest()
        {
            _storage = new TempDirectory();

            // cam1 is assigned to a disabled plugin: deleting it must be refused.
            _settings = new TestSettings()
                .Camera(new Onvif.Open.Recorder.Configuration.CameraOptions { Id = "cam1", RtspUrl = "rtsp://127.0.0.1:1/stream", Username = "admin", Password = "secret" })
                .Plugin(new Onvif.Open.Recorder.Store.StoredPlugin { Folder = "Mailer", Enabled = false, Cameras = { "cam1" } });

            _factory = Factory();
            _client = TestSettings.Authenticate(_factory.CreateClient());
        }

        #endregion

        #region Private Variables

        private readonly TempDirectory _storage;
        private readonly TestSettings _settings;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        #endregion

        #region Private Methods

        private WebApplicationFactory<Program> Factory()
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Recording:Retention:MinFreeSpaceGB", "0");
                builder.UseSetting("Recording:MinReconnectDelay", "00:00:00.050");
                builder.UseSetting("Recording:MaxReconnectDelay", "00:00:00.100");
            });
        }

        private static object RtspCamera(string name, string policyId = TestSettings.AlwaysPolicyId, string password = null, bool enabled = true, string siteId = TestSettings.SiteId) => new
        {
            name,
            siteId,
            policyId,
            rtspUrl = "rtsp://127.0.0.1:1/" + name.Replace(' ', '-'),
            username = "viewer",
            password,
            enabled
        };

        /// <summary>Adds a camera through the API and returns the id the recorder gave it.</summary>
        private async Task<string> AddAsync(object camera)
        {
            var created = await _client.PostAsJsonAsync("/api/config/cameras", camera);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
            Assert.Equal($"/api/config/cameras/{id}", created.Headers.Location.OriginalString);
            return id;
        }

        private async Task<JsonElement> Json(string uri)
        {
            return JsonDocument.Parse(await _client.GetStringAsync(uri)).RootElement;
        }

        private async Task<JsonElement> Status(string cameraId)
        {
            return (await Json("/api/cameras")).EnumerateArray().FirstOrDefault(c => c.GetProperty("cameraId").GetString() == cameraId);
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task CamerasShouldBeListedWithoutPasswords()
        {
            var body = await _client.GetStringAsync("/api/config/cameras");
            var camera = JsonDocument.Parse(body).RootElement.EnumerateArray().Single();

            Assert.Equal("cam1", camera.GetProperty("id").GetString());
            Assert.True(camera.GetProperty("hasPassword").GetBoolean());
            Assert.False(camera.TryGetProperty("password", out _));
            Assert.DoesNotContain("secret", body);
        }

        [Fact]
        public async Task CamerasShouldBeAddedChangedAndRemovedWhileRunning()
        {
            var id = await AddAsync(RtspCamera("Back door", password: "p4ss"));
            Assert.True(Guid.TryParse(id, out _), "the recorder assigns a UUID");

            // Recording starts right away (the camera is unreachable here, so it keeps reconnecting), named and placed.
            var status = await Status(id);
            Assert.Equal("Continuous", status.GetProperty("mode").GetString());
            Assert.Equal("Back door", status.GetProperty("name").GetString());
            Assert.Equal(TestSettings.SiteId, status.GetProperty("siteId").GetString());

            // Renamed and moved to the events-only policy; without a password the stored one is kept.
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/config/cameras/{id}", RtspCamera("Rear door", policyId: TestSettings.EventsPolicyId))).StatusCode);
            Assert.Equal("Triggered", (await Status(id)).GetProperty("mode").GetString());
            var saved = await Json($"/api/config/cameras/{id}");
            Assert.Equal("Rear door", saved.GetProperty("name").GetString());
            Assert.Equal(TestSettings.EventsPolicyId, saved.GetProperty("policyId").GetString());
            Assert.True(saved.GetProperty("hasPassword").GetBoolean());
            Assert.False(saved.TryGetProperty("organizationId", out _));

            // An empty password removes it.
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/config/cameras/{id}", RtspCamera("Rear door", password: ""))).StatusCode);
            Assert.False((await Json($"/api/config/cameras/{id}")).GetProperty("hasPassword").GetBoolean());

            Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/config/cameras/{id}")).StatusCode);
            Assert.Equal(JsonValueKind.Undefined, (await Status(id)).ValueKind);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/config/cameras/{id}")).StatusCode);
        }

        [Fact]
        public async Task CamerasWithoutAPolicyShouldGetTheDefault()
        {
            var id = await AddAsync(new { name = "Gate", siteId = TestSettings.SiteId, rtspUrl = "rtsp://127.0.0.1:1/gate" });
            Assert.Equal(TestSettings.AlwaysPolicyId, (await Json($"/api/config/cameras/{id}")).GetProperty("policyId").GetString());
        }

        [Fact]
        public async Task InvalidChangesShouldBeRefused()
        {
            var invalid = await _client.PostAsJsonAsync("/api/config/cameras", new { name = " ", siteId = "elsewhere", policyId = "none", onvifUrl = "http://192.168.1.10", rtspUrl = "http://not-rtsp" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(errors, e => e.StartsWith("Name", StringComparison.Ordinal));
            Assert.Contains(errors, e => e.Contains("sites"));
            Assert.Contains(errors, e => e.Contains("recording policies"));
            Assert.Contains(errors, e => e.Contains("exactly one of OnvifUrl or RtspUrl"));

            Assert.Equal(HttpStatusCode.NotFound, (await _client.PutAsJsonAsync("/api/config/cameras/nope", RtspCamera("nope"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/config/cameras/nope")).StatusCode);

            // Assigned to a (disabled) plugin: removing it would widen the plugin to all cameras.
            var assigned = await _client.DeleteAsync("/api/config/cameras/cam1");
            Assert.Equal(HttpStatusCode.Conflict, assigned.StatusCode);
            Assert.Contains("Mailer", await assigned.Content.ReadAsStringAsync());
            Assert.NotEqual(JsonValueKind.Undefined, (await Status("cam1")).ValueKind);
        }

        [Fact]
        public async Task PluginAssignmentsShouldBeSavedWithWriteOnlySettings()
        {
            var saved = await _client.PutAsJsonAsync("/api/config/plugins/Onvif.Open.Plugins.Sample", new { enabled = true, cameras = new[] { "CAM1" }, settings = new System.Collections.Generic.Dictionary<string, string> { ["Smtp:Password"] = "mail-secret" } });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

            var body = await _client.GetStringAsync("/api/config/plugins");
            Assert.DoesNotContain("mail-secret", body);
            var plugin = JsonDocument.Parse(body).RootElement.EnumerateArray().Single(p => p.GetProperty("folder").GetString() == "Onvif.Open.Plugins.Sample");
            Assert.Equal("Smtp:Password", plugin.GetProperty("settings")[0].GetString());
            Assert.Equal("cam1", plugin.GetProperty("cameras")[0].GetString(), ignoreCase: true);

            // Settings left out are kept.
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync("/api/config/plugins/Onvif.Open.Plugins.Sample", new { enabled = false, cameras = new[] { "cam1" } })).StatusCode);
            Assert.Equal("mail-secret", (await _settings.Store.GetPluginsAsync()).Single(p => p.Folder == "Onvif.Open.Plugins.Sample").Settings["Smtp:Password"]);

            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync("/api/config/plugins/Sample", new { cameras = new[] { "nope" } })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync("/api/config/plugins/a..b", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/config/plugins/Onvif.Open.Plugins.Sample")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync("/api/config/plugins/Onvif.Open.Plugins.Sample")).StatusCode);
        }

        [Fact]
        public async Task DisabledCamerasShouldBeKeptButNotRecorded()
        {
            var id = await AddAsync(RtspCamera("Spare", enabled: false));

            Assert.Equal(JsonValueKind.Undefined, (await Status(id)).ValueKind);
            Assert.False((await Json($"/api/config/cameras/{id}")).GetProperty("enabled").GetBoolean());

            // Enabling it starts recording.
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync($"/api/config/cameras/{id}", RtspCamera("Spare"))).StatusCode);
            Assert.NotEqual(JsonValueKind.Undefined, (await Status(id)).ValueKind);
        }

        [Fact]
        public async Task ChangesShouldSurviveARestart()
        {
            var id = await AddAsync(RtspCamera("Yard", policyId: TestSettings.EventsPolicyId, password: "p4ss"));
            _client.Dispose();
            _factory.Dispose();

            using (var restarted = Factory())
            using (var client = TestSettings.Authenticate(restarted.CreateClient()))
            {
                var ids = JsonDocument.Parse(await client.GetStringAsync("/api/config/cameras")).RootElement.EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
                Assert.Equal(new[] { "cam1", id }, ids);

                // Started again with its policy applied.
                var recording = JsonDocument.Parse(await client.GetStringAsync("/api/cameras")).RootElement.EnumerateArray().ToList();
                Assert.Equal(new[] { "cam1", id }.OrderBy(i => i), recording.Select(c => c.GetProperty("cameraId").GetString()).OrderBy(i => i));
                Assert.Equal("Triggered", recording.Single(c => c.GetProperty("cameraId").GetString() == id).GetProperty("mode").GetString());
            }
        }

        public void Dispose()
        {
            _client.Dispose();
            _factory.Dispose();
            _storage.Dispose();
            _settings.Dispose();
        }

        #endregion
    }
}
