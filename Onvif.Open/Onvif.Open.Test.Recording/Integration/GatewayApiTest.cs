// Sarthak Killedar
// 2026-11-09

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// The recorder as its organization's gateway: created at setup, reporting its health, and holding the settings
    /// that moved out of appsettings.json (storage limits, cloud backup, discovery).
    /// </summary>
    public sealed class GatewayApiTest : IDisposable
    {
        #region Constructor

        public GatewayApiTest()
        {
            _storage = new TempDirectory();
        }

        #endregion

        #region Private Variables

        private readonly TempDirectory _storage;

        #endregion

        #region Private Methods

        private WebApplicationFactory<Program> Factory(TestSettings settings) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
            });

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
            JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private static async Task<JsonElement> GatewayAsync(HttpClient client) =>
            JsonDocument.Parse(await client.GetStringAsync("/api/gateways")).RootElement.EnumerateArray().Single();

        private static string[] Names(JsonElement array) => array.EnumerateArray().Select(e => e.GetString()).ToArray();

        #endregion

        #region Public Methods

        [Fact]
        public async Task SetupShouldCreateTheRecordersOwnGateway()
        {
            using (var settings = new TestSettings(seeded: false))
            using (var factory = Factory(settings))
            using (var client = factory.CreateClient())
            {
                var done = await client.PostAsJsonAsync("/api/setup", new { organizationName = "Riverside Shops", region = "us", siteName = "Head office", timeZone = "UTC", userName = "dana", password = "Dana-Owner-Pass-1" });
                Assert.Equal(HttpStatusCode.OK, done.StatusCode);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await ReadAsync(done)).GetProperty("accessToken").GetString());

                var gateway = await GatewayAsync(client);
                Assert.True(gateway.GetProperty("embedded").GetBoolean());
                Assert.Equal(("Local", "Online"), (gateway.GetProperty("location").GetString(), gateway.GetProperty("status").GetString()));
                Assert.False(string.IsNullOrEmpty(gateway.GetProperty("version").GetString()));
                Assert.Equal(0, gateway.GetProperty("health").GetProperty("cameras").GetInt32());
                Assert.True(gateway.GetProperty("health").GetProperty("freeBytes").GetInt64() > 0);
                Assert.Equal(1, gateway.GetProperty("storage").GetProperty("minFreeSpaceGB").GetDouble());
                Assert.True(gateway.GetProperty("discovery").GetProperty("multicast").GetBoolean());

                // Cameras added from now on are recorded by it.
                var site = JsonDocument.Parse(await client.GetStringAsync("/api/sites")).RootElement[0].GetProperty("id").GetString();
                var camera = await client.PostAsJsonAsync("/api/config/cameras", new { name = "Gate", siteId = site, rtspUrl = "rtsp://127.0.0.1:9/stream", enabled = false });
                Assert.Equal(HttpStatusCode.Created, camera.StatusCode);
                Assert.Equal(gateway.GetProperty("id").GetString(), (await ReadAsync(camera)).GetProperty("gatewayId").GetString());
            }
        }

        [Fact]
        public async Task AdministratorsShouldChangeTheGatewaysSettings()
        {
            using (var settings = new TestSettings())
            using (var factory = Factory(settings))
            using (var client = TestSettings.Authenticate(factory.CreateClient()))
            {
                var uri = $"/api/gateways/{TestSettings.GatewayId}";
                var gateway = await GatewayAsync(client);
                Assert.Equal(("Test gateway", false), (gateway.GetProperty("name").GetString(), gateway.GetProperty("restartRequired").GetBoolean()));

                // Storage limits apply at the next retention run.
                var limits = await client.PutAsJsonAsync(uri, new { name = "Back office", storage = new { minFreeSpaceGB = 10, maxSizePerCameraGB = 50 } });
                Assert.Equal(HttpStatusCode.OK, limits.StatusCode);
                Assert.Equal(50, (await ReadAsync(limits)).GetProperty("storage").GetProperty("maxSizePerCameraGB").GetDouble());
                Assert.Equal(10, factory.Services.GetRequiredService<GatewayService>().Limits.MinFreeSpaceGB);

                // The backup applies when the recorder starts next; its settings are never returned.
                var share = Path.Combine(_storage.Path, "nas-share");
                var backup = await client.PutAsJsonAsync(uri, new { backup = new { provider = "FileSystem", keepLocalCopyHours = 24, settings = new System.Collections.Generic.Dictionary<string, string> { ["Path"] = share } } });
                Assert.Equal(HttpStatusCode.OK, backup.StatusCode);
                var body = await backup.Content.ReadAsStringAsync();
                Assert.DoesNotContain("nas-share", body);
                var saved = JsonDocument.Parse(body).RootElement;
                Assert.True(saved.GetProperty("restartRequired").GetBoolean());
                Assert.Equal(new[] { "Path" }, Names(saved.GetProperty("backup").GetProperty("settings")));
                Assert.Equal(10, saved.GetProperty("storage").GetProperty("minFreeSpaceGB").GetDouble());

                // Without settings, the stored ones are kept.
                var prefix = await client.PutAsJsonAsync(uri, new { backup = new { provider = "FileSystem", prefix = "back-office", keepLocalCopyHours = 24 } });
                Assert.Equal(new[] { "Path" }, Names((await ReadAsync(prefix)).GetProperty("backup").GetProperty("settings")));
                Assert.Equal(share, (await settings.Gateways.FindAsync(TestSettings.GatewayId)).Backup.Settings["Path"]);

                Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(uri, new { backup = new { provider = "FileSystem", settings = new { } } })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(uri, new { discovery = new { multicast = true, addresses = new[] { "camera.local" } } })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(uri, new { name = " " })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(uri, new { storage = new { minFreeSpaceGB = -1 } })).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/gateways/{Guid.NewGuid()}", new { name = "Nope" })).StatusCode);

                // Removing the backup: nothing left to apply.
                var removedResponse = await client.PutAsJsonAsync(uri, new { backup = new { provider = "" } });
                Assert.True(removedResponse.IsSuccessStatusCode, await removedResponse.Content.ReadAsStringAsync());
                var removed = await ReadAsync(removedResponse);
                Assert.False(removed.GetProperty("restartRequired").GetBoolean());

                await factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
                var audited = JsonDocument.Parse(await client.GetStringAsync("/api/audit?action=gateway")).RootElement;
                Assert.Equal(4, audited.GetArrayLength());
                Assert.All(audited.EnumerateArray(), e => Assert.Equal("Back office", e.GetProperty("targetName").GetString()));
            }
        }

        [Fact]
        public async Task ViewersShouldSeeTheGatewayButNotItsSettings()
        {
            using (var settings = new TestSettings())
            using (var factory = Factory(settings))
            using (var owner = TestSettings.Authenticate(factory.CreateClient()))
            using (var viewer = factory.CreateClient())
            {
                Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/users", new { userName = "vera", password = "Temporary-Pass-1", role = "Viewer", sites = Array.Empty<string>() })).StatusCode);
                var first = await TestSettings.SignInAsync(viewer, "vera", "Temporary-Pass-1");
                viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
                var changed = await viewer.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Temporary-Pass-1", newPassword = "Vera-Own-Pass-2" });
                viewer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await ReadAsync(changed)).GetProperty("accessToken").GetString());

                var gateway = await GatewayAsync(viewer);
                Assert.Equal("Online", gateway.GetProperty("status").GetString());
                Assert.Equal(JsonValueKind.Null, gateway.GetProperty("storage").ValueKind);
                Assert.Equal(JsonValueKind.Null, gateway.GetProperty("backup").ValueKind);
                Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/gateways/{TestSettings.GatewayId}", new { name = "Mine" })).StatusCode);
            }
        }

        public void Dispose()
        {
            _storage.Dispose();
        }

        #endregion
    }
}
