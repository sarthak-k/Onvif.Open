// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Onvif.Open.Recorder.Api;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// A gateway the cloud runs for a customer whose cameras are on the internet: started with its own token, running
    /// once it reports, started again with a new token, stopped when removed; its cameras must have public addresses.
    /// </summary>
    public sealed class CloudGatewayTest : IDisposable
    {
        public CloudGatewayTest()
        {
            _settings = new TestSettings();
            _storage = new TempDirectory();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Cloud:Enabled", "true");
                builder.UseSetting("Cloud:Region", "us");
                builder.UseSetting("Cloud:Regions:us:WebUrl", "https://us.cameras.example.com");
                builder.UseSetting("Cloud:Gateways:ControlPlaneUrl", "https://gateways.us.cameras.example.com/");
                builder.UseSetting("Cloud:Gateways:StartTimeout", "00:10:00");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ICloudGatewayHost>();
                    services.AddSingleton<ICloudGatewayHost>(_host);
                });
            });
            _admin = TestSettings.Authenticate(_factory.CreateClient());
        }

        public void Dispose()
        {
            _admin.Dispose();
            _factory.Dispose();
            _settings.Dispose();
            _storage.Dispose();
        }

        private readonly TestSettings _settings;
        private readonly TempDirectory _storage;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _admin;
        private readonly FakeHost _host = new FakeHost();

        private sealed class FakeHost : ICloudGatewayHost
        {
            public bool IsConfigured => true;
            public ConcurrentQueue<CloudGatewayLaunch> Started { get; } = new ConcurrentQueue<CloudGatewayLaunch>();
            public ConcurrentQueue<string> Stopped { get; } = new ConcurrentQueue<string>();
            public string Fail { get; set; }

            public Task StartAsync(CloudGatewayLaunch launch, CancellationToken cancellationToken = default)
            {
                if (Fail != null)
                {
                    throw new InvalidOperationException(Fail);
                }

                Started.Enqueue(launch);
                return Task.CompletedTask;
            }

            public Task StopAsync(string gatewayId, CancellationToken cancellationToken = default)
            {
                Stopped.Enqueue(gatewayId);
                return Task.CompletedTask;
            }
        }

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private async Task<string> HostStateAsync(string id) => (await ReadAsync(await _admin.GetAsync($"/api/gateways/{id}"))).GetProperty("hosting").GetProperty("state").GetString();

        private async Task<HttpStatusCode> HeartbeatAsync(string token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, "/api/gateways/heartbeat") { Content = JsonContent.Create(new { version = "1.0" }) })
            {
                request.Headers.Add(GatewayEndpoints.GatewayTokenHeader, token);
                return (await _factory.CreateClient().SendAsync(request)).StatusCode;
            }
        }

        [Fact]
        public async Task ACloudGatewayShouldStartReportAndStop()
        {
            Assert.True((await ReadAsync(await _admin.GetAsync("/api/cloud"))).GetProperty("cloudGateways").GetBoolean());

            // Started with its own identity, pointed at this control plane.
            var created = await _admin.PostAsync("/api/gateways/cloud", null);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var gateway = await ReadAsync(created);
            var id = gateway.GetProperty("id").GetString();
            Assert.Equal(("Cloud gateway", "Cloud", "Starting"), (gateway.GetProperty("name").GetString(), gateway.GetProperty("location").GetString(), gateway.GetProperty("hosting").GetProperty("state").GetString()));
            var launch = Assert.Single(_host.Started);
            Assert.Equal((id, "https://gateways.us.cameras.example.com"), (launch.GatewayId, launch.ControlPlaneUrl));

            // One per organization.
            Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync("/api/gateways/cloud", null)).StatusCode);

            // Running once it reports.
            Assert.Equal(HttpStatusCode.OK, await HeartbeatAsync(launch.Token));
            Assert.Equal("Running", await HostStateAsync(id));

            // Its cameras are on the internet: other addresses are refused when the camera is saved.
            var site = TestSettings.SiteId;
            var refused = await _admin.PostAsJsonAsync("/api/config/cameras", new { name = "Door", siteId = site, gatewayId = id, rtspUrl = "rtsp://192.168.1.20/stream" });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("RTSP URL: 192.168.1.20 is not on the internet", await refused.Content.ReadAsStringAsync());
            refused = await _admin.PostAsJsonAsync("/api/config/cameras", new { name = "Door", siteId = site, gatewayId = id, rtspUrl = "rtsp://8.8.8.8/stream", subRtspUrl = "rtsp://169.254.169.254/latest" });
            Assert.Contains("Smaller stream for grids: 169.254.169.254", await refused.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Created, (await _admin.PostAsJsonAsync("/api/config/cameras", new { name = "Door", siteId = site, gatewayId = id, rtspUrl = "rtsp://8.8.8.8/stream" })).StatusCode);

            // Started again: stopped first, a new token; the old one stops working.
            var again = await _admin.PostAsync($"/api/gateways/{id}/start", null);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);
            Assert.Equal("Starting", (await ReadAsync(again)).GetProperty("hosting").GetProperty("state").GetString());
            Assert.Equal(id, Assert.Single(_host.Stopped));
            var relaunch = _host.Started.Last();
            Assert.NotEqual(launch.Token, relaunch.Token);
            Assert.Equal(HttpStatusCode.Unauthorized, await HeartbeatAsync(launch.Token));
            Assert.Equal(HttpStatusCode.Conflict, (await _admin.PostAsync($"/api/gateways/{id}/start", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, await HeartbeatAsync(relaunch.Token));

            // Removed (once its cameras are): stopped, and its token is gone.
            var cameras = await ReadAsync(await _admin.GetAsync("/api/config/cameras"));
            foreach (var camera in cameras.EnumerateArray().Where(c => c.GetProperty("gatewayId").GetString() == id))
            {
                Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/config/cameras/{camera.GetProperty("id").GetString()}")).StatusCode);
            }

            // The gateway reports with the identity it was started with, and never asks for a pairing code.
            var gatewayClient = new ControlPlaneClient(_factory.CreateClient(), new ConfiguredControlPlaneCredentials(id, relaunch.Token), () => new GatewayHealth(), "cloud", "Cloud", Microsoft.Extensions.Logging.Abstractions.NullLogger<ControlPlaneClient>.Instance);
            Assert.Equal(ControlPlaneClient.HeartbeatInterval, await gatewayClient.StepAsync());

            Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/gateways/{id}")).StatusCode);
            Assert.Equal(ControlPlaneClient.RetryInterval, await gatewayClient.StepAsync());
            Assert.Equal(ControlPlaneClient.RetryInterval, await gatewayClient.StepAsync());
            Assert.Null(gatewayClient.PairingCode);
            Assert.True(gatewayClient.IsPaired);
            Assert.Equal(2, _host.Stopped.Count(s => s == id));
            Assert.Equal(HttpStatusCode.Unauthorized, await HeartbeatAsync(relaunch.Token));

            await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
            var audit = (await ReadAsync(await _admin.GetAsync("/api/audit?action=gateway"))).EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
            Assert.Contains("gateway.cloud.create", audit);
            Assert.Contains("gateway.cloud.start", audit);
        }

        [Fact]
        public async Task AGatewayThatCannotStartShouldSayWhyAndStartAgain()
        {
            _host.Fail = "docker exited with 125: no such image";
            var gateway = await ReadAsync(await _admin.PostAsJsonAsync("/api/gateways/cloud", new { name = "Shop cameras" }));
            var id = gateway.GetProperty("id").GetString();
            Assert.Equal(("Shop cameras", "Failed", "docker exited with 125: no such image"), (gateway.GetProperty("name").GetString(), gateway.GetProperty("hosting").GetProperty("state").GetString(), gateway.GetProperty("hosting").GetProperty("error").GetString()));

            _host.Fail = null;
            Assert.Equal(HttpStatusCode.OK, (await _admin.PostAsync($"/api/gateways/{id}/start", null)).StatusCode);
            Assert.Equal("Starting", await HostStateAsync(id));

            // One that never reports has failed once the start timeout passes.
            var cloud = _factory.Services.GetRequiredService<CloudGateways>();
            Assert.Equal(0, await cloud.CheckAsync());
            await _factory.Services.GetRequiredService<IGatewayStore>().UpdateHostAsync(id, "Starting", null, DateTime.UtcNow.AddMinutes(-11));
            Assert.Equal(1, await cloud.CheckAsync());
            var failed = (await ReadAsync(await _admin.GetAsync($"/api/gateways/{id}"))).GetProperty("hosting");
            Assert.Equal("Failed", failed.GetProperty("state").GetString());
            Assert.Contains("did not connect within 10 minutes", failed.GetProperty("error").GetString());

            // ... unless it connects after all.
            Assert.Equal(HttpStatusCode.OK, await HeartbeatAsync(_host.Started.Last().Token));
            Assert.Equal("Running", await HostStateAsync(id));
        }

        [Fact]
        public async Task OnlyOrganizationAdministratorsShouldStartCloudGateways()
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsync("/api/gateways/cloud", null)).StatusCode);
            Assert.Empty(_host.Started);
        }
    }
}
