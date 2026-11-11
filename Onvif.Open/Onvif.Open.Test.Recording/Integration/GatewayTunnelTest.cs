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
                // The control plane's clock: a gateway that goes silent is noticed when it moves on.
                builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(_clock));
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
                builder.UseSetting("ControlPlane:EventOutboxFile", Path.Combine(_gwStorage.Path, "event-outbox.jsonl"));
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

        private readonly ManualTimeProvider _clock = new ManualTimeProvider();
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

        private async Task<string> RemoteCameraAsync(string gatewayId, string name = "Dock")
        {
            var cameraId = (await ReadAsync(await _owner.PostAsJsonAsync("/api/config/cameras", new { name, siteId = TestSettings.SiteId, rtspUrl = $"rtsp://127.0.0.1:1/{name}", gatewayId }))).GetProperty("id").GetString();
            var assigned = _gateway.Services.GetRequiredService<AssignedCameras>();
            await Eventually.True(() => assigned.CameraIds.Contains(cameraId), "the gateway got its camera");
            return cameraId;
        }

        private async Task<List<JsonElement>> EventsAsync(string cameraId) =>
            (await ReadAsync(await _owner.GetAsync($"/api/events?cameraId={cameraId}"))).EnumerateArray().ToList();

        [Fact]
        public async Task EventsShouldReachTheControlPlaneAndRaiseItsAlerts()
        {
            var gatewayId = await PairAsync();
            var cameraId = await RemoteCameraAsync(gatewayId);
            Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/alert-rules", new { name = "Offline", categories = new[] { "offline" } })).StatusCode);

            // At the gateway: the camera drops. The event waits in the outbox (on disk) until the control plane has it.
            var recorder = _gateway.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<Onvif.Open.Recorder.Events.EventRecorder>().Single();
            var outbox = _gateway.Services.GetRequiredService<EventOutbox>();
            var client = _gateway.Services.GetRequiredService<ControlPlaneClient>();
            var lost = DateTimeOffset.UtcNow;
            recorder.OnStateChanged(this, new CameraStateChange(cameraId, Onvif.Open.Core.Recording.RecordingState.Recording, Onvif.Open.Core.Recording.RecordingState.Reconnecting, "Connection refused", lost));

            // Handed over (by the gateway's own upload every 2 s, or this one): stored at the control plane (its site, not
            // the gateway's), and its alert rule fires. (The outbox on disk: TheOutboxShouldOutliveARestart.)
            await Eventually.True(() => client.UploadEventsAsync().GetAwaiter().GetResult() >= 0 && EventsAsync(cameraId).GetAwaiter().GetResult().Count == 1, "the event was handed over");
            var events = await EventsAsync(cameraId);
            var offline = Assert.Single(events);
            Assert.Equal(("offline", TestSettings.SiteId, JsonValueKind.Null), (offline.GetProperty("category").GetString(), offline.GetProperty("siteId").GetString(), offline.GetProperty("endTime").ValueKind));
            JsonElement alerts = default;
            await Eventually.True(() => (alerts = ReadAsync(_owner.GetAsync("/api/alerts").Result).Result).GetArrayLength() == 1, "the control plane alerted");
            Assert.Equal(("Camera offline", "Dock"), (alerts[0].GetProperty("title").GetString(), alerts[0].GetProperty("cameraName").GetString()));

            // It records again: the end follows the same way.
            recorder.OnStateChanged(this, new CameraStateChange(cameraId, Onvif.Open.Core.Recording.RecordingState.Reconnecting, Onvif.Open.Core.Recording.RecordingState.Recording, null, lost.AddSeconds(40)));
            await Eventually.True(() => client.UploadEventsAsync().GetAwaiter().GetResult() >= 0 && EventsAsync(cameraId).GetAwaiter().GetResult().Single().GetProperty("endTime").ValueKind != JsonValueKind.Null, "the end was handed over");
            Assert.Equal(40, (Assert.Single(await EventsAsync(cameraId)).GetProperty("endTime").GetDateTimeOffset() - lost).TotalSeconds, 1);

            await Eventually.True(() => outbox.Count == 0 && File.ReadAllLines(Path.Combine(_gwStorage.Path, "event-outbox.jsonl")).Length == 0, "nothing is left in the outbox");

            // Handed over again (the answer to a stored batch was lost): stored once, and no second alert.
            var again = new Onvif.Open.Recorder.Store.EventRecord
            {
                Id = offline.GetProperty("id").GetString(), OrganizationId = TestSettings.OrganizationId, SiteId = TestSettings.SiteId, CameraId = cameraId,
                Category = "offline", Severity = "Warning", Title = "Camera offline", Source = "recorder", StartTime = lost.UtcDateTime
            };
            var controlPlaneEvents = _controlPlane.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<Onvif.Open.Recorder.Events.EventRecorder>().Single();
            await controlPlaneEvents.ImportAsync(new[] { again }, Array.Empty<(string, string, DateTime, DateTime)>());
            Assert.Single(await EventsAsync(cameraId));
            await Task.Delay(300);
            Assert.Equal(1, (await ReadAsync(await _owner.GetAsync("/api/alerts"))).GetArrayLength());
        }

        [Fact]
        public async Task TheOutboxShouldOutliveARestart()
        {
            // What waited on disk when the gateway stopped is there when it starts again, oldest first, until stored.
            var path = Path.Combine(_gwStorage.Path, "restart-outbox.jsonl");
            var json = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var start = DateTime.UtcNow;
            File.WriteAllLines(path, new[]
            {
                System.Text.Json.JsonSerializer.Serialize(new UpstreamEvent(UpstreamEvent.Started, null, "e1", "cam", start, null), json),
                System.Text.Json.JsonSerializer.Serialize(new UpstreamEvent(UpstreamEvent.Ended, null, "e1", "cam", start, start.AddSeconds(5)), json)
            });

            var recorder = _gateway.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<Onvif.Open.Recorder.Events.EventRecorder>().Single();
            var outbox = new EventOutbox(recorder, _gateway.Services.GetRequiredService<AssignedCameras>(), path, null);
            Assert.Equal(2, outbox.Count);
            Assert.Equal(new[] { UpstreamEvent.Started, UpstreamEvent.Ended }, outbox.Peek(10).Select(e => e.Kind));

            outbox.Remove(1);
            Assert.Equal(UpstreamEvent.Ended, Assert.Single(new EventOutbox(recorder, _gateway.Services.GetRequiredService<AssignedCameras>(), path, null).Peek(10)).Kind);
            outbox.Remove(1);
            Assert.Empty(File.ReadAllLines(path));
            await Task.CompletedTask;
        }

        [Fact]
        public void AGatewayShouldOnlyReportItsOwnCameras()
        {
            var directory = new Onvif.Open.Recorder.Tenancy.CameraDirectory();
            var policy = new Onvif.Open.Recorder.Store.RecordingPolicyRecord { Id = "p", Mode = "Always", ContinuousRetentionDays = 7, EventRetentionDays = 30 };
            directory.Set(new Onvif.Open.Recorder.Configuration.CameraOptions { Id = "mine", Name = "Mine", SiteId = "site-a", GatewayId = "gw-1" }, "org-1", policy);
            directory.Set(new Onvif.Open.Recorder.Configuration.CameraOptions { Id = "theirs", Name = "Theirs", SiteId = "site-b", GatewayId = "gw-2" }, "org-1", policy);
            var gateway = new Gateway { Id = "gw-1", OrganizationId = "org-1" };
            var start = DateTime.UtcNow;
            Onvif.Open.Recorder.Store.EventRecord Started(string camera) => new Onvif.Open.Recorder.Store.EventRecord { Id = Guid.NewGuid().ToString(), OrganizationId = "elsewhere", SiteId = "nowhere", CameraId = camera, Category = "aliens", Severity = "Apocalyptic", Title = new string('x', 500), StartTime = start };

            var changes = new[]
            {
                new UpstreamEvent(UpstreamEvent.Started, Started("mine"), null, "mine", start, null) with { Id = "e1" },
                new UpstreamEvent(UpstreamEvent.Started, Started("theirs"), "e2", "theirs", start, null),
                new UpstreamEvent(UpstreamEvent.Started, Started("nope"), "e3", "nope", start, null),
                new UpstreamEvent(UpstreamEvent.Ended, null, "e1", "mine", start, start.AddSeconds(5)),
                new UpstreamEvent(UpstreamEvent.Ended, null, "e4", "mine", null, null),
            };
            var (adds, closes, rejected) = Upstream.Accept(gateway, changes, directory);

            // Only its own camera, with this organization's site; the rest is cleaned up or refused.
            var add = Assert.Single(adds);
            Assert.Equal(("e1", "org-1", "site-a", "other", "Info", 200), (add.Id, add.OrganizationId, add.SiteId, add.Category, add.Severity, add.Title.Length));
            Assert.Equal("e1", Assert.Single(closes).Id);
            Assert.Equal(3, rejected);
        }

        [Fact]
        public async Task ASiteGoingDarkShouldMarkItsCamerasOffline()
        {
            var gatewayId = await PairAsync();
            var cameraId = await RemoteCameraAsync(gatewayId);
            var watch = _controlPlane.Services.GetRequiredService<GatewayWatch>();
            Assert.Empty(await watch.CheckAsync());

            // The site loses power: nothing more from its gateway.
            await _gateway.DisposeAsync();
            await Eventually.True(() => !_controlPlane.Services.GetRequiredService<TunnelRegistry>().IsConnected(gatewayId), "the tunnel closed");
            _clock.Advance(TimeSpan.FromMinutes(3));
            Assert.Equal(new[] { cameraId }, await watch.CheckAsync());
            Assert.Equal(new[] { cameraId }, await watch.CheckAsync());

            List<JsonElement> events = null;
            await Eventually.True(() => (events = EventsAsync(cameraId).Result).Count == 1, "the camera was marked offline");
            Assert.Equal(("offline", "Gateway offline", JsonValueKind.Null), (events[0].GetProperty("category").GetString(), events[0].GetProperty("title").GetString(), events[0].GetProperty("endTime").ValueKind));

            // Back: the outage ends.
            var gateways = _controlPlane.Services.GetRequiredService<IGatewayStore>();
            await _controlPlane.Services.GetRequiredService<GatewayService>().ReportAsync(await gateways.FindAsync(gatewayId), GatewayService.Version, DateTime.UtcNow, new GatewayHealth(), default);
            Assert.Empty(await watch.CheckAsync());
            await Eventually.True(() => EventsAsync(cameraId).Result.Single().GetProperty("endTime").ValueKind != JsonValueKind.Null, "the outage ended");
        }

        [SkippableFact]
        public async Task ExportsOfAGatewaysCameraShouldBeMadeFromItsRecordings()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            var gatewayId = await PairAsync();
            var cameraId = await RemoteCameraAsync(gatewayId);
            var start = DateTimeOffset.UtcNow.AddHours(-1);
            RealMedia.WriteRecordings(_gwStorage.Path, cameraId, start);

            // Asked for at the control plane, which has no video of it: the gateway sends the clip, it is watermarked here.
            var queued = await _owner.PostAsJsonAsync("/api/exports", new { cameraId, from = start.AddSeconds(1), to = start.AddSeconds(8) });
            Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            var id = (await ReadAsync(queued)).GetProperty("id").GetString();
            JsonElement export = default;
            await Eventually.True(() => (export = ReadAsync(_owner.GetAsync($"/api/exports/{id}").Result).Result).GetProperty("status").GetString() is "Ready" or "Failed", "the export finished", 60000);
            Assert.True(export.GetProperty("status").GetString() == "Ready", export.ToString());

            var file = Path.Combine(_cpStorage.Path, "remote.mp4");
            await File.WriteAllBytesAsync(file, await _owner.GetByteArrayAsync($"/api/exports/{id}/video.mp4"));
            var (streams, duration) = await RealMedia.ProbeAsync(file);
            Assert.Contains("h264", streams);
            Assert.InRange(duration, 5.5, 8.5);

            // Nothing recorded then: the gateway says so, and the export says why.
            var empty = (await ReadAsync(await _owner.PostAsJsonAsync("/api/exports", new { cameraId, from = start.AddDays(-1), to = start.AddDays(-1).AddSeconds(5) }))).GetProperty("id").GetString();
            await Eventually.True(() => (export = ReadAsync(_owner.GetAsync($"/api/exports/{empty}").Result).Result).GetProperty("status").GetString() == "Failed", "the export failed", 30000);
            Assert.Contains("Nothing was recorded", export.GetProperty("error").GetString());
        }
    }
}
