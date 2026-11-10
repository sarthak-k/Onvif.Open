// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recorder.Api;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Recorder.Hosting;
using Onvif.Open.Recorder.Tunnel;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// A gateway on another machine, end to end: two recorders in one process, a control plane and a gateway that only
    /// ever connects out to it. The gateway pairs, opens its tunnel, records the cameras it is given, reports them; the
    /// control plane relays their video and controls through the tunnel.
    /// </summary>
    public sealed class GatewayTunnelTest : IAsyncDisposable
    {
        public GatewayTunnelTest()
        {
            _cpSettings = new TestSettings();
            _cpStorage = new TempDirectory();
            _controlPlane = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _cpSettings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _cpStorage.Path);
            });
            _owner = TestSettings.Authenticate(_controlPlane.CreateClient());

            _gwSettings = new TestSettings();
            _gwStorage = new TempDirectory();
            _gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _gwSettings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _gwStorage.Path);
                builder.UseSetting("ControlPlane:Url", _controlPlane.Server.BaseAddress.ToString());
                builder.UseSetting("ControlPlane:CredentialsFile", Path.Combine(_gwStorage.Path, "control-plane.json"));
                builder.UseSetting("ControlPlane:CamerasFile", Path.Combine(_gwStorage.Path, "assigned-cameras.json"));
                builder.UseSetting("Gateway:Name", "Warehouse box");
                builder.UseSetting("LiveView:StartTimeout", "00:00:05");
                // The gateway reaches the control plane only through these: HTTP and its tunnel.
                builder.ConfigureTestServices(services => services.AddSingleton(new ControlPlaneTransport(
                    () => _controlPlane.Server.CreateHandler(),
                    (uri, token, ct) =>
                    {
                        var client = _controlPlane.Server.CreateWebSocketClient();
                        client.ConfigureRequest = request => request.Headers[GatewayEndpoints.GatewayTokenHeader] = token;
                        return client.ConnectAsync(uri, ct);
                    })));
            });
        }

        public async ValueTask DisposeAsync()
        {
            _owner.Dispose();
            await _gateway.DisposeAsync();
            await _controlPlane.DisposeAsync();
            _gwSettings.Dispose();
            _cpSettings.Dispose();
            _gwStorage.Dispose();
            _cpStorage.Dispose();
        }

        private readonly TestSettings _cpSettings;
        private readonly TempDirectory _cpStorage;
        private readonly WebApplicationFactory<Program> _controlPlane;
        private readonly HttpClient _owner;
        private readonly TestSettings _gwSettings;
        private readonly TempDirectory _gwStorage;
        private readonly WebApplicationFactory<Program> _gateway;

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        /// <summary>Starts the gateway, enters its code at the control plane, and waits for its tunnel.</summary>
        private async Task<string> PairAsync()
        {
            _gateway.CreateClient().Dispose();
            var client = _gateway.Services.GetRequiredService<ControlPlaneClient>();
            await Eventually.True(() => client.PairingCode != null, "the gateway asked for a code", 15000);

            var paired = await _owner.PostAsJsonAsync("/api/gateways/pair", new { code = client.PairingCode });
            Assert.Equal(HttpStatusCode.Created, paired.StatusCode);
            var id = (await ReadAsync(paired)).GetProperty("id").GetString();

            var tunnels = _controlPlane.Services.GetRequiredService<TunnelRegistry>();
            await Eventually.True(() => tunnels.IsConnected(id) && client.IsConnected, "the gateway opened its tunnel", 20000);
            Assert.Equal(TunnelStates.Open, client.TunnelState);
            return id;
        }

        private async Task<JsonElement> CameraAsync(string id) =>
            (await ReadAsync(await _owner.GetAsync("/api/cameras"))).EnumerateArray().Single(c => c.GetProperty("cameraId").GetString() == id);

        [Fact]
        public async Task AGatewayShouldRecordItsCamerasAndRelayThemWithoutAnyPortOpen()
        {
            var gatewayId = await PairAsync();

            // A camera on the gateway: the gateway records it, the control plane does not.
            var created = await _owner.PostAsJsonAsync("/api/config/cameras", new { name = "Dock", siteId = TestSettings.SiteId, rtspUrl = "rtsp://127.0.0.1:1/dock", password = "dock-secret", gatewayId });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var cameraId = (await ReadAsync(created)).GetProperty("id").GetString();
            Assert.Equal(gatewayId, (await ReadAsync(await _owner.GetAsync($"/api/config/cameras/{cameraId}"))).GetProperty("gatewayId").GetString());

            var assigned = _gateway.Services.GetRequiredService<AssignedCameras>();
            await Eventually.True(() => assigned.CameraIds.Contains(cameraId), "the gateway got its camera");
            Assert.DoesNotContain(_controlPlane.Services.GetRequiredService<Onvif.Open.Core.Abstract.Interface.Recording.IRecordingManager>().GetStatus(), s => s.CameraId == cameraId);

            // Listed at the control plane: offline until the gateway reports, then as the gateway sees it.
            Assert.Equal("Offline", (await CameraAsync(cameraId)).GetProperty("state").GetString());
            await _gateway.Services.GetRequiredService<ControlPlaneClient>().StepAsync();
            var reported = await CameraAsync(cameraId);
            Assert.NotEqual("Offline", reported.GetProperty("state").GetString());
            Assert.Equal((gatewayId, "Dock"), (reported.GetProperty("gatewayId").GetString(), reported.GetProperty("name").GetString()));

            // Controls go through the tunnel: the gateway answers (an RTSP camera cannot pan).
            var ptz = await _owner.GetAsync($"/api/cameras/{cameraId}/ptz");
            Assert.Equal(HttpStatusCode.NotImplemented, ptz.StatusCode);
            Assert.Contains("not an ONVIF camera", await ptz.Content.ReadAsStringAsync());

            // The set is kept at the gateway for outages, encrypted (it holds the camera's password).
            Assert.DoesNotContain("dock-secret", await File.ReadAllTextAsync(Path.Combine(_gwStorage.Path, "assigned-cameras.json")));

            // Removed at the control plane: the gateway stops recording it.
            Assert.Equal(HttpStatusCode.NoContent, (await _owner.DeleteAsync($"/api/config/cameras/{cameraId}")).StatusCode);
            await Eventually.True(() => !assigned.CameraIds.Contains(cameraId), "the gateway let the camera go");
        }

        [SkippableFact]
        public async Task LiveVideoShouldStreamThroughTheTunnelAndBeAudited()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            var gatewayId = await PairAsync();
            var cameraId = (await ReadAsync(await _owner.PostAsJsonAsync("/api/config/cameras", new { name = "Dock", siteId = TestSettings.SiteId, rtspUrl = "rtsp://127.0.0.1:1/dock", gatewayId }))).GetProperty("id").GetString();
            var assigned = _gateway.Services.GetRequiredService<AssignedCameras>();
            await Eventually.True(() => assigned.CameraIds.Contains(cameraId), "the gateway got its camera");

            // The camera sends video at the gateway (25 fps).
            var live = _gateway.Services.GetRequiredService<LiveViewService>();
            Assert.NotNull(live.WatchMp4(cameraId));
            using (var stop = new System.Threading.CancellationTokenSource())
            {
                var feed = Task.Run(async () =>
                {
                    foreach (var frame in RealMedia.Frames.OfType<VideoFrame>())
                    {
                        live.OnFrame(this, new CameraFrame(cameraId, frame, DateTimeOffset.UtcNow));
                        await Task.Delay(40, stop.Token);
                    }
                }, stop.Token);

                // A viewer asks the control plane; the video comes from the gateway, fragment by fragment.
                using (var response = await _owner.GetAsync($"/api/cameras/{cameraId}/live.mp4", HttpCompletionOption.ResponseHeadersRead))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
                    Assert.StartsWith("avc1.", response.Headers.GetValues(PlaybackEndpoints.CodecHeader).Single());

                    var body = await response.Content.ReadAsStreamAsync();
                    var boxes = new List<string>();
                    var header = new byte[8];
                    while (boxes.Count(b => b == "moof") < 5)
                    {
                        await body.ReadExactlyAsync(header);
                        boxes.Add(System.Text.Encoding.ASCII.GetString(header, 4, 4));
                        await body.ReadExactlyAsync(new byte[System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header) - 8]);
                    }

                    Assert.Equal(new[] { "ftyp", "moov", "moof", "mdat" }, boxes.Take(4));
                }

                stop.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => feed);
            }

            // The viewer left: the gateway let go of the stream too.
            await Eventually.True(() => live.WatchMp4(cameraId).ViewerCount == 0, "the gateway released the viewer");

            // Who watched is in the control plane's audit log, as for its own cameras.
            await _controlPlane.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
            var audit = await ReadAsync(await _owner.GetAsync("/api/audit?action=video.live"));
            Assert.Equal(("Dock", TestSettings.AdminName), (audit[0].GetProperty("targetName").GetString(), audit[0].GetProperty("userName").GetString()));
        }

        [Fact]
        public async Task CamerasOfAGatewayThatIsGoneShouldSaySo()
        {
            var gatewayId = await PairAsync();
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.PostAsJsonAsync("/api/config/cameras", new { name = "Gate", siteId = TestSettings.SiteId, rtspUrl = "rtsp://127.0.0.1:1/gate", gatewayId = "nope" })).StatusCode);
            var cameraId = (await ReadAsync(await _owner.PostAsJsonAsync("/api/config/cameras", new { name = "Gate", siteId = TestSettings.SiteId, rtspUrl = "rtsp://127.0.0.1:1/gate", gatewayId }))).GetProperty("id").GetString();

            await _gateway.DisposeAsync();
            var tunnels = _controlPlane.Services.GetRequiredService<TunnelRegistry>();
            await Eventually.True(() => !tunnels.IsConnected(gatewayId), "the tunnel closed");

            var answer = await _owner.GetAsync($"/api/cameras/{cameraId}/live.mp4");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
            Assert.Contains("gateway that records this camera is offline", await answer.Content.ReadAsStringAsync());
            Assert.Equal("Offline", (await CameraAsync(cameraId)).GetProperty("state").GetString());

            // A camera for that gateway is tested from its network, not here: not while it is offline.
            var test = await _owner.PostAsJsonAsync($"/api/config/cameras/test?gatewayId={gatewayId}", new { rtspUrl = "rtsp://127.0.0.1:1/gate" });
            Assert.Equal(HttpStatusCode.ServiceUnavailable, test.StatusCode);
            Assert.Contains("cannot test the camera", await test.Content.ReadAsStringAsync());
        }
    }
}
