// Sarthak Killedar
// 2026-11-08

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Sign-in, roles, revocation and the protections around them.
    /// </summary>
    public sealed class SecurityApiTest : IDisposable
    {
        #region Constructor

        public SecurityApiTest()
        {
            _storage = new TempDirectory();
            _settings = new TestSettings().Camera(new CameraOptions { Id = "cam1", RtspUrl = "rtsp://127.0.0.1:1/stream" });
        }

        #endregion

        #region Private Variables

        private const string ViewerPassword = "Viewer-Pass-2026";
        private const string OperatorPassword = "Operator-Pass-2026";

        private readonly TempDirectory _storage;
        private readonly TestSettings _settings;

        #endregion

        #region Private Methods

        private WebApplicationFactory<Program> Factory(params (string Key, string Value)[] settings)
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);

                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }
            });
        }

        private static HttpClient With(HttpClient client, string accessToken)
        {
            client.DefaultRequestHeaders.Authorization = accessToken == null ? null : new AuthenticationHeaderValue("Bearer", accessToken);
            return client;
        }

        /// <summary>Creates a user as admin; the user signs in and replaces the admin-given password.</summary>
        private static async Task<string> UserAsync(WebApplicationFactory<Program> factory, string userName, string role, string password)
        {
            using (var admin = TestSettings.Authenticate(factory.CreateClient()))
            {
                var created = await admin.PostAsJsonAsync("/api/users", new { userName, password = "Temporary-Pass-1", role });
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            }

            using (var client = factory.CreateClient())
            {
                var first = await TestSettings.SignInAsync(client, userName, "Temporary-Pass-1");
                With(client, first.AccessToken);

                // Until the password is changed, only the account endpoints work.
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/cameras")).StatusCode);
                Assert.True(JsonDocument.Parse(await client.GetStringAsync("/api/auth/me")).RootElement.GetProperty("mustChangePassword").GetBoolean());

                var changed = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Temporary-Pass-1", newPassword = password });
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                return JsonDocument.Parse(await changed.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString();
            }
        }

        private static async Task<string> UserIdAsync(HttpClient admin, string userName)
        {
            return JsonDocument.Parse(await admin.GetStringAsync("/api/users")).RootElement.EnumerateArray()
                .Single(u => u.GetProperty("userName").GetString() == userName).GetProperty("id").GetString();
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task EverythingButHealthAndSignInShouldNeedASignedInUser()
        {
            using (var factory = Factory())
            using (var client = factory.CreateClient())
            {
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

                foreach (var path in new[] { "/api/cameras", "/api/config/cameras", "/api/users", "/api/storage", "/api/auth/me", "/api/cameras/cam1/live.m3u8", "/api/cameras/cam1/live.mp4", "/api/cameras/cam1/clip" })
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
                }

                Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "wrong-password" })).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { userName = "nobody", password = "wrong-password" })).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await With(client, "not-a-token").GetAsync("/api/cameras")).StatusCode);
            }
        }

        [Fact]
        public void OnlyHealthSignInAndGatewaysShouldAllowAnonymousAccess()
        {
            using (var factory = Factory())
            {
                factory.CreateClient().Dispose();
                var anonymous = factory.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
                    .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
                    .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null)
                    .Select(e => e.RoutePattern.RawText)
                    .OrderBy(p => p, StringComparer.Ordinal);

                // Setup only works while there are no users (and with Security:SetupToken when one is configured).
                // Gateways ask for a pairing code (rate-limited; pairs nothing until an administrator enters it), collect
                // their token with the pairing's secret, and report, hand over events and open their tunnel with their token. A share link downloads one export until it
                // expires or is revoked.
                Assert.Equal(new[] { "/api/auth/login", "/api/auth/login/two-factor", "/api/auth/refresh", "/api/auth/sso/", "/api/auth/sso/callback", "/api/auth/sso/exchange", "/api/auth/sso/saml/acs", "/api/auth/sso/saml/metadata", "/api/auth/sso/start", "/api/cloud", "/api/gateways/events", "/api/gateways/heartbeat", "/api/gateways/pairing", "/api/gateways/pairing/{id}", "/api/gateways/tunnel", "/api/setup", "/api/setup", "/api/shared/{token}/video.mp4", "/api/signup", "/api/signup/verify", "/health" }, anonymous);
            }
        }

        [Fact]
        public async Task RolesShouldGrantWhatTheyNeed()
        {
            using (var factory = Factory())
            {
                using (var viewer = With(factory.CreateClient(), await UserAsync(factory, "watcher", "Viewer", ViewerPassword)))
                {
                    Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/cameras")).StatusCode);
                    Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/cameras/cam1/segments")).StatusCode);
                    Assert.Equal("Viewer", JsonDocument.Parse(await viewer.GetStringAsync("/api/auth/me")).RootElement.GetProperty("role").GetString());

                    Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/cameras/cam1/trigger", new { seconds = 5 })).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/cameras/cam1/ptz/stop", new { })).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/config/cameras")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/users")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync("/api/cameras/cam1/segments/x.ts")).StatusCode);
                }

                using (var operatorClient = With(factory.CreateClient(), await UserAsync(factory, "guard", "Operator", OperatorPassword)))
                {
                    Assert.NotEqual(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync("/api/cameras/cam1/trigger", new { seconds = 5 })).StatusCode);
                    Assert.NotEqual(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync("/api/cameras/cam1/ptz/stop", new { })).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/config/cameras")).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync("/api/retention", null)).StatusCode);
                    Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/discovery")).StatusCode);
                }

                using (var admin = TestSettings.Authenticate(factory.CreateClient()))
                {
                    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/config/cameras")).StatusCode);
                    Assert.Equal(new[] { "admin", "guard", "watcher" }, JsonDocument.Parse(await admin.GetStringAsync("/api/users")).RootElement.EnumerateArray().Select(u => u.GetProperty("userName").GetString()));
                }
            }
        }

        [Fact]
        public async Task ChangesToAUserShouldEndItsSessions()
        {
            using (var factory = Factory())
            using (var admin = TestSettings.Authenticate(factory.CreateClient()))
            {
                var token = await UserAsync(factory, "watcher", "Viewer", ViewerPassword);

                using (var viewer = factory.CreateClient())
                {
                    var tokens = await TestSettings.SignInAsync(viewer, "watcher", ViewerPassword);
                    With(viewer, tokens.AccessToken);
                    Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/cameras")).StatusCode);

                    // A refresh token gives a new access token.
                    var refreshed = await viewer.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
                    Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);

                    var id = await UserIdAsync(admin, "watcher");
                    Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{id}", new { enabled = false })).StatusCode);

                    Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.GetAsync("/api/cameras")).StatusCode);
                    Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken })).StatusCode);
                    Assert.Equal(HttpStatusCode.Unauthorized, (await viewer.PostAsJsonAsync("/api/auth/login", new { userName = "watcher", password = ViewerPassword })).StatusCode);
                }
            }
        }

        [Fact]
        public async Task RepeatedWrongPasswordsShouldLockTheAccount()
        {
            using (var factory = Factory())
            using (var client = factory.CreateClient())
            {
                for (var i = 0; i < 5; i++)
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "wrong-password" })).StatusCode);
                }

                // Even the right password is refused while locked.
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { userName = TestSettings.AdminName, password = TestSettings.AdminPassword })).StatusCode);
            }
        }

        [Fact]
        public async Task SignInsShouldBeRateLimited()
        {
            using (var factory = Factory(("Security:SignInsPerMinute", "3")))
            using (var client = factory.CreateClient())
            {
                for (var i = 0; i < 3; i++)
                {
                    await client.PostAsJsonAsync("/api/auth/login", new { userName = "nobody", password = "wrong-password" });
                }

                Assert.Equal((HttpStatusCode)429, (await client.PostAsJsonAsync("/api/auth/login", new { userName = "nobody", password = "wrong-password" })).StatusCode);
            }
        }

        [Fact]
        public async Task AnEnabledOwnerShouldAlwaysRemain()
        {
            using (var factory = Factory())
            using (var admin = TestSettings.Authenticate(factory.CreateClient()))
            {
                var id = await UserIdAsync(admin, "admin");

                Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/users/{id}", new { role = "Viewer" })).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/users/{id}", new { enabled = false })).StatusCode);
                Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/users/{id}")).StatusCode);

                var invalid = await admin.PostAsJsonAsync("/api/users", new { userName = "bad name", password = "short", role = "Root" });
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
                Assert.Equal(3, JsonDocument.Parse(await invalid.Content.ReadAsStringAsync()).RootElement.GetProperty("errors").GetArrayLength());
                Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/users", new { userName = "ADMIN", password = "Another-Pass-1", role = "Viewer" })).StatusCode);
            }
        }

        [Fact]
        public async Task VideoMayCarryTheTokenInTheUrlButNothingElse()
        {
            using (var factory = Factory())
            using (var client = factory.CreateClient())
            {
                var token = Uri.EscapeDataString((await TestSettings.SignInAsync(client)).AccessToken);

                // Not a video request: a token in the URL is ignored.
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/cameras?access_token={token}")).StatusCode);

                // A native player: the playlist's own URIs carry the token on, and work without a header.
                var day = System.IO.Path.Combine(_storage.Path, "cam1", "2026-11-01");
                System.IO.Directory.CreateDirectory(day);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(day, "cam1_20261101T080000000Z.ts"), Enumerable.Repeat((byte)0x47, 188 * 3).ToArray());

                var playlist = await client.GetAsync($"/api/cameras/cam1/recordings.m3u8?from=2026-11-01T07:00:00Z&to=2026-11-01T09:00:00Z&access_token={token}");
                Assert.Equal(HttpStatusCode.OK, playlist.StatusCode);
                var segment = (await playlist.Content.ReadAsStringAsync()).Split('\n').Single(l => l.StartsWith("segments/", StringComparison.Ordinal));
                Assert.Equal($"segments/cam1_20261101T080000000Z.ts?access_token={token}", segment);
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/cameras/cam1/" + segment)).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/cameras/cam1/segments/cam1_20261101T080000000Z.ts")).StatusCode);

                Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync($"/api/cameras/cam1/live.m3u8?access_token={token}")).StatusCode);
            }
        }

        [Fact]
        public async Task OnlyConfiguredOriginsShouldBeAllowedByCors()
        {
            using (var factory = Factory(("Security:AllowedOrigins:0", "https://cameras.example.com/")))
            using (var client = factory.CreateClient())
            {
                var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/cameras");
                preflight.Headers.Add("Origin", "https://cameras.example.com");
                preflight.Headers.Add("Access-Control-Request-Method", "GET");
                preflight.Headers.Add("Access-Control-Request-Headers", "authorization");
                var allowed = await client.SendAsync(preflight);
                Assert.Equal("https://cameras.example.com", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());

                var other = new HttpRequestMessage(HttpMethod.Options, "/api/cameras");
                other.Headers.Add("Origin", "https://evil.example.com");
                other.Headers.Add("Access-Control-Request-Method", "GET");
                Assert.False((await client.SendAsync(other)).Headers.Contains("Access-Control-Allow-Origin"));
            }
        }

        public void Dispose()
        {
            _storage.Dispose();
            _settings.Dispose();
        }

        #endregion
    }
}
