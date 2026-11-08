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

        private static object RtspCamera(string id, string mode = "Continuous", string password = null, bool enabled = true) => new
        {
            id,
            rtspUrl = "rtsp://127.0.0.1:1/" + id,
            username = "viewer",
            password,
            mode,
            enabled
        };

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
            var created = await _client.PostAsJsonAsync("/api/config/cameras", RtspCamera("cam2", password: "p4ss"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal("/api/config/cameras/cam2", created.Headers.Location.OriginalString);

            // Recording starts right away (the camera is unreachable here, so it keeps reconnecting).
            Assert.Equal("Continuous", (await Status("cam2")).GetProperty("mode").GetString());

            // Without a password the stored one is kept; an empty one removes it.
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync("/api/config/cameras/cam2", RtspCamera("cam2", mode: "Triggered"))).StatusCode);
            Assert.Equal("Triggered", (await Status("cam2")).GetProperty("mode").GetString());
            Assert.True((await Json("/api/config/cameras/cam2")).GetProperty("hasPassword").GetBoolean());

            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync("/api/config/cameras/cam2", RtspCamera("cam2", password: ""))).StatusCode);
            Assert.False((await Json("/api/config/cameras/cam2")).GetProperty("hasPassword").GetBoolean());

            Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync("/api/config/cameras/cam2")).StatusCode);
            Assert.Equal(JsonValueKind.Undefined, (await Status("cam2")).ValueKind);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/config/cameras/cam2")).StatusCode);
        }

        [Fact]
        public async Task InvalidChangesShouldBeRefused()
        {
            var invalid = await _client.PostAsJsonAsync("/api/config/cameras", new { id = "bad id", onvifUrl = "http://192.168.1.10", rtspUrl = "http://not-rtsp" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var errors = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains(errors, e => e.Contains("is invalid"));
            Assert.Contains(errors, e => e.Contains("exactly one of OnvifUrl or RtspUrl"));

            Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsJsonAsync("/api/config/cameras", RtspCamera("CAM1"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PutAsJsonAsync("/api/config/cameras/cam1", RtspCamera("other"))).StatusCode);
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
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/config/cameras", RtspCamera("spare", enabled: false))).StatusCode);

            Assert.Equal(JsonValueKind.Undefined, (await Status("spare")).ValueKind);
            Assert.False((await Json("/api/config/cameras/spare")).GetProperty("enabled").GetBoolean());

            // Enabling it starts recording.
            Assert.Equal(HttpStatusCode.OK, (await _client.PutAsJsonAsync("/api/config/cameras/spare", RtspCamera("spare"))).StatusCode);
            Assert.NotEqual(JsonValueKind.Undefined, (await Status("spare")).ValueKind);
        }

        [Fact]
        public async Task ChangesShouldSurviveARestart()
        {
            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/config/cameras", RtspCamera("cam2", password: "p4ss"))).StatusCode);
            _client.Dispose();
            _factory.Dispose();

            using (var restarted = Factory())
            using (var client = TestSettings.Authenticate(restarted.CreateClient()))
            {
                var ids = JsonDocument.Parse(await client.GetStringAsync("/api/config/cameras")).RootElement.EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
                Assert.Equal(new[] { "cam1", "cam2" }, ids);

                var recording = JsonDocument.Parse(await client.GetStringAsync("/api/cameras")).RootElement.EnumerateArray().Select(c => c.GetProperty("cameraId").GetString()).ToList();
                Assert.Equal(new[] { "cam1", "cam2" }, recording);
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
