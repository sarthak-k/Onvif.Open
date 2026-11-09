// Sarthak Killedar
// 2026-11-09

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// First-run setup, the organization, sites, recording policies, and what users limited to some sites can see.
    /// </summary>
    public sealed class TenancyApiTest : IDisposable
    {
        #region Constructor

        public TenancyApiTest()
        {
            _storage = new TempDirectory();
        }

        #endregion

        #region Private Variables

        private readonly TempDirectory _storage;
        private TestSettings _settings;

        private static readonly object ValidSetup = new
        {
            organizationName = "Riverside Shops",
            region = "asia",
            siteName = "Head office",
            timeZone = "Asia/Kolkata",
            userName = "dana",
            password = "Dana-Owner-Pass-1"
        };

        #endregion

        #region Private Methods

        private WebApplicationFactory<Program> Factory(TestSettings settings, params (string Key, string Value)[] extra)
        {
            _settings = settings;
            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Recording:Retention:MinFreeSpaceGB", "0");
                foreach (var (key, value) in extra)
                {
                    builder.UseSetting(key, value);
                }
            });
        }

        private static async Task<JsonElement> JsonAsync(HttpClient client, string uri) => JsonDocument.Parse(await client.GetStringAsync(uri)).RootElement;

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        /// <summary>A member who has chosen their own password, signed in.</summary>
        private static async Task<HttpClient> MemberAsync(WebApplicationFactory<Program> factory, HttpClient admin, string userName, string role, params string[] sites)
        {
            var created = await admin.PostAsJsonAsync("/api/users", new { userName, password = "Temporary-Pass-1", role, sites });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            var client = factory.CreateClient();
            var first = await TestSettings.SignInAsync(client, userName, "Temporary-Pass-1");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            var changed = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Temporary-Pass-1", newPassword = userName + "-Own-Pass-2" });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await ReadAsync(changed)).GetProperty("accessToken").GetString());
            return client;
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task SetupShouldCreateTheOrganizationOnce()
        {
            using (var factory = Factory(new TestSettings(seeded: false)))
            using (var client = factory.CreateClient())
            {
                var setup = await JsonAsync(client, "/api/setup");
                Assert.True(setup.GetProperty("required").GetBoolean());
                Assert.Equal(new[] { "asia", "us" }, setup.GetProperty("regions").EnumerateArray().Select(r => r.GetProperty("id").GetString()));

                var invalid = await client.PostAsJsonAsync("/api/setup", new { organizationName = "", region = "mars", siteName = "", timeZone = "Nowhere/City", userName = "dana", password = "short" });
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                Assert.Equal(5, (await ReadAsync(invalid)).GetProperty("errors").GetArrayLength());

                var done = await client.PostAsJsonAsync("/api/setup", ValidSetup);
                Assert.Equal(HttpStatusCode.OK, done.StatusCode);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await ReadAsync(done)).GetProperty("accessToken").GetString());

                var me = await JsonAsync(client, "/api/auth/me");
                Assert.Equal("Owner", me.GetProperty("role").GetString());
                Assert.False(me.GetProperty("mustChangePassword").GetBoolean());
                Assert.Equal("Riverside Shops", me.GetProperty("organization").GetProperty("name").GetString());
                Assert.Equal("asia", me.GetProperty("organization").GetProperty("region").GetString());

                var site = (await JsonAsync(client, "/api/sites")).EnumerateArray().Single();
                Assert.Equal(("Head office", "Asia/Kolkata"), (site.GetProperty("name").GetString(), site.GetProperty("timeZone").GetString()));

                // The agreed defaults: continuous video 7 days, video around events 30 days.
                var policies = (await JsonAsync(client, "/api/recording-policies")).EnumerateArray().ToList();
                Assert.Equal(new[] { "Always", "EventsOnly" }, policies.Select(p => p.GetProperty("mode").GetString()));
                Assert.True(policies[0].GetProperty("isDefault").GetBoolean());
                Assert.All(policies, p => Assert.Equal((7, 30), (p.GetProperty("continuousRetentionDays").GetInt32(), p.GetProperty("eventRetentionDays").GetInt32())));

                Assert.False((await JsonAsync(client, "/api/setup")).GetProperty("required").GetBoolean());
                Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/setup", ValidSetup)).StatusCode);
            }
        }

        [Fact]
        public async Task AConfiguredSetupTokenShouldBeRequired()
        {
            using (var factory = Factory(new TestSettings(seeded: false), ("Security:SetupToken", "let-me-in")))
            using (var client = factory.CreateClient())
            {
                Assert.True((await JsonAsync(client, "/api/setup")).GetProperty("tokenRequired").GetBoolean());
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/setup", ValidSetup)).StatusCode);

                var withToken = JsonSerializer.SerializeToNode(ValidSetup).AsObject();
                withToken["setupToken"] = "let-me-in";
                Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/setup", withToken)).StatusCode);
            }
        }

        [Fact]
        public async Task AConfiguredOwnerShouldBeCreatedAtStartup()
        {
            using (var factory = Factory(new TestSettings(seeded: false), ("Security:InitialAdmin:Password", "Configured-Pass-1"), ("Security:InitialAdmin:Region", "asia")))
            using (var client = factory.CreateClient())
            {
                Assert.False((await JsonAsync(client, "/api/setup")).GetProperty("required").GetBoolean());
                TestSettings.Authenticate(client, "admin", "Configured-Pass-1");

                var me = await JsonAsync(client, "/api/auth/me");
                Assert.Equal("Owner", me.GetProperty("role").GetString());
                Assert.Equal("asia", me.GetProperty("organization").GetProperty("region").GetString());
            }
        }

        [Fact]
        public async Task SitesShouldBeManagedWithTheirRules()
        {
            using (var factory = Factory(new TestSettings().Camera(new CameraOptions { Id = "cam1", RtspUrl = "rtsp://127.0.0.1:1/cam1" })))
            using (var owner = TestSettings.Authenticate(factory.CreateClient()))
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/sites", new { name = "Warehouse", timeZone = "Mars/Olympus" })).StatusCode);

                var created = await owner.PostAsJsonAsync("/api/sites", new { name = "Warehouse", timeZone = "Europe/Paris", address = "1 Quai, Lyon" });
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                var id = (await ReadAsync(created)).GetProperty("id").GetString();

                var renamed = await owner.PutAsJsonAsync($"/api/sites/{id}", new { name = "Lyon warehouse", timeZone = "Europe/Paris" });
                Assert.Equal("Lyon warehouse", (await ReadAsync(renamed)).GetProperty("name").GetString());

                // A site with cameras stays; an empty one goes; the last one stays.
                Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/sites/{TestSettings.SiteId}")).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/sites/{id}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync($"/api/sites/{id}")).StatusCode);
            }
        }

        [Fact]
        public async Task ChangedPoliciesShouldApplyToTheirCameras()
        {
            using (var factory = Factory(new TestSettings().Camera(new CameraOptions { Id = "cam1", RtspUrl = "rtsp://127.0.0.1:1/cam1" })))
            using (var owner = TestSettings.Authenticate(factory.CreateClient()))
            {
                Assert.Equal("Continuous", (await JsonAsync(owner, "/api/cameras/cam1")).GetProperty("mode").GetString());

                var changed = await owner.PutAsJsonAsync($"/api/recording-policies/{TestSettings.AlwaysPolicyId}", new { mode = "EventsOnly", continuousRetentionDays = 14 });
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                Assert.Equal("Triggered", (await JsonAsync(owner, "/api/cameras/cam1")).GetProperty("mode").GetString());

                // In use, or the default: cannot be deleted. A new default replaces the old one.
                Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/recording-policies/{TestSettings.AlwaysPolicyId}")).StatusCode);
                var created = await owner.PostAsJsonAsync("/api/recording-policies", new { name = "Long", mode = "Always", continuousRetentionDays = 30, eventRetentionDays = 90, isDefault = true });
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                var defaults = (await JsonAsync(owner, "/api/recording-policies")).EnumerateArray().Where(p => p.GetProperty("isDefault").GetBoolean()).Select(p => p.GetProperty("name").GetString());
                Assert.Equal(new[] { "Long" }, defaults);

                Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/recording-policies/{TestSettings.EventsPolicyId}")).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/recording-policies", new { name = "Bad", mode = "Sometimes", eventRetentionDays = 0 })).StatusCode);
            }
        }

        [Fact]
        public async Task UsersLimitedToSitesShouldSeeOnlyThose()
        {
            var settings = new TestSettings();
            var lyon = settings.AddSite("Lyon");
            settings.Camera(new CameraOptions { Id = "main-cam", RtspUrl = "rtsp://127.0.0.1:1/main" })
                    .Camera(new CameraOptions { Id = "lyon-cam", SiteId = lyon, RtspUrl = "rtsp://127.0.0.1:1/lyon" });

            using (var factory = Factory(settings))
            using (var owner = TestSettings.Authenticate(factory.CreateClient()))
            using (var viewer = await MemberAsync(factory, owner, "lyon-viewer", "Viewer", lyon))
            using (var admin = await MemberAsync(factory, owner, "lyon-admin", "Admin", lyon))
            {
                Assert.Equal(new[] { "lyon-cam" }, (await JsonAsync(viewer, "/api/cameras")).EnumerateArray().Select(c => c.GetProperty("cameraId").GetString()));
                Assert.Equal(new[] { lyon }, (await JsonAsync(viewer, "/api/sites")).EnumerateArray().Select(s => s.GetProperty("id").GetString()));
                Assert.Equal(new[] { lyon }, (await JsonAsync(viewer, "/api/auth/me")).GetProperty("sites").EnumerateArray().Select(s => s.GetString()));

                // Another site's camera does not exist for them, whatever the route.
                Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/cameras/lyon-cam/segments")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/cameras/main-cam/segments")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/api/cameras/main-cam/recordings.m3u8")).StatusCode);

                // A site-limited admin configures their site's cameras, but not organization-wide settings.
                Assert.Equal(new[] { "lyon-cam" }, (await JsonAsync(admin, "/api/config/cameras")).EnumerateArray().Select(c => c.GetProperty("id").GetString()));
                Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/config/cameras/main-cam")).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/config/cameras", new { name = "Sneaky", siteId = TestSettings.SiteId, rtspUrl = "rtsp://127.0.0.1:1/x" })).StatusCode);
                Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/config/cameras", new { name = "Dock", siteId = lyon, rtspUrl = "rtsp://127.0.0.1:1/dock" })).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/users")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/sites", new { name = "Mine", timeZone = "UTC" })).StatusCode);

                // Sites that do not exist cannot be given.
                Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/users", new { userName = "x", password = "Temporary-Pass-1", role = "Viewer", sites = new[] { "nowhere" } })).StatusCode);
            }
        }

        [Fact]
        public async Task OnlyOwnersShouldManageOwnersAndTheOrganization()
        {
            using (var factory = Factory(new TestSettings()))
            using (var owner = TestSettings.Authenticate(factory.CreateClient()))
            using (var admin = await MemberAsync(factory, owner, "ada", "Admin"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/users", new { userName = "boss", password = "Temporary-Pass-1", role = "Owner" })).StatusCode);

                var users = (await JsonAsync(admin, "/api/users")).EnumerateArray().ToList();
                var ownerId = users.Single(u => u.GetProperty("role").GetString() == "Owner").GetProperty("id").GetString();
                Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync($"/api/users/{ownerId}", new { enabled = false })).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await admin.DeleteAsync($"/api/users/{ownerId}")).StatusCode);

                Assert.Equal("Test organization", (await JsonAsync(admin, "/api/organization")).GetProperty("name").GetString());
                Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync("/api/organization", new { name = "Mine" })).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/organization", new { name = "Renamed" })).StatusCode);
                Assert.Equal("Renamed", (await JsonAsync(admin, "/api/organization")).GetProperty("name").GetString());
            }
        }

        public void Dispose()
        {
            _settings?.Dispose();
            _storage.Dispose();
        }

        #endregion
    }
}
