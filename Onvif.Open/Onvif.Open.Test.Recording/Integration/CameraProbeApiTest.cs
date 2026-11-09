// Sarthak Killedar
// 2026-11-08

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Test.Camera.Fakes;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// The connection test of the configuration API: a camera tried before it is saved, or with its stored password.
    /// </summary>
    public sealed class CameraProbeApiTest : IAsyncLifetime
    {
        #region Constructor

        public CameraProbeApiTest()
        {
            _camera = new FakeRtspServer("viewer", "camera-secret");
            _storage = new TempDirectory();

            // Stored but not recorded, so only the probe talks to the fake camera.
            _settings = new TestSettings().Camera(new Onvif.Open.Recorder.Configuration.CameraOptions
            {
                Id = "gate",
                Enabled = false,
                RtspUrl = _camera.StreamUri.ToString(),
                Username = "viewer",
                Password = "camera-secret"
            });

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
            });
            _client = TestSettings.Authenticate(_factory.CreateClient());
        }

        #endregion

        #region Private Variables

        private readonly FakeRtspServer _camera;
        private readonly TempDirectory _storage;
        private readonly TestSettings _settings;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        #endregion

        #region Private Methods

        private async Task<JsonElement> ProbeAsync(object camera)
        {
            var response = await _client.PostAsJsonAsync("/api/config/cameras/test", camera);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        }

        #endregion

        #region Public Methods

        public Task InitializeAsync() => Task.CompletedTask;

        [Fact]
        public async Task ReachableStreamsShouldReportTheirCodec()
        {
            var result = await ProbeAsync(new { rtspUrl = _camera.StreamUri.ToString(), username = "viewer", password = "camera-secret" });

            Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
            Assert.Equal("H264", result.GetProperty("stream").GetProperty("codec").GetString());
        }

        [Fact]
        public async Task WrongPasswordsShouldBeReported()
        {
            var result = await ProbeAsync(new { rtspUrl = _camera.StreamUri.ToString(), username = "viewer", password = "wrong" });

            Assert.False(result.GetProperty("ok").GetBoolean());
            Assert.Equal("unauthorized", result.GetProperty("kind").GetString());
        }

        [Fact]
        public async Task StoredPasswordsShouldBeUsedWhenNoneIsGiven()
        {
            var result = await ProbeAsync(new { id = "gate", rtspUrl = _camera.StreamUri.ToString(), username = "viewer", password = (string)null });

            Assert.True(result.GetProperty("ok").GetBoolean(), result.ToString());
        }

        [Fact]
        public async Task UnreachableCamerasShouldBeReported()
        {
            var result = await ProbeAsync(new { rtspUrl = "rtsp://127.0.0.1:9/stream" });

            Assert.False(result.GetProperty("ok").GetBoolean());
            Assert.Equal("unreachable", result.GetProperty("kind").GetString());
            Assert.False(string.IsNullOrEmpty(result.GetProperty("error").GetString()));
        }

        [Fact]
        public async Task ProbesShouldNeedOneUrl()
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/config/cameras/test", new { username = "viewer" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync("/api/config/cameras/test", new { onvifUrl = "http://10.0.0.1/onvif/device_service", rtspUrl = "rtsp://10.0.0.1/s" })).StatusCode);
        }

        [Fact]
        public async Task ProbesShouldBeForAdministratorsOnly()
        {
            using (var anonymous = _factory.CreateClient())
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/config/cameras/test", new { rtspUrl = _camera.StreamUri.ToString() })).StatusCode);
            }

            Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/users", new { userName = "olivia", password = "Operator-Pass-2026", role = "Operator" })).StatusCode);
            using (var operatorClient = _factory.CreateClient())
            {
                // New users choose their own password first; until then every call is refused anyway.
                var tokens = await TestSettings.SignInAsync(operatorClient, "olivia", "Operator-Pass-2026");
                operatorClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokens.AccessToken);
                var changed = await operatorClient.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Operator-Pass-2026", newPassword = "Olivia-Own-Pass-2026" });
                Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
                var accessToken = JsonDocument.Parse(await changed.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString();
                operatorClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                Assert.Equal(HttpStatusCode.OK, (await operatorClient.GetAsync("/api/cameras")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync("/api/config/cameras/test", new { rtspUrl = _camera.StreamUri.ToString() })).StatusCode);
            }
        }

        public async Task DisposeAsync()
        {
            _client.Dispose();
            _factory.Dispose();
            await _camera.DisposeAsync();
            _storage.Dispose();
            _settings.Dispose();
        }

        #endregion
    }
}
