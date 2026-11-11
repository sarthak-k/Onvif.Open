// Sarthak Killedar
// 2026-11-11

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Core.Networking;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>The rule is for the whole process, so tests that set it run alone.</summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class CameraNetworkCollection
    {
        public const string Name = "Camera network (process-wide)";
    }

    /// <summary>
    /// A gateway in the cloud (Gateway:Location=Cloud) never connects to the network it runs in, whatever address a
    /// camera has or hands back: here a "camera" on loopback that must never see a connection.
    /// </summary>
    [Collection(CameraNetworkCollection.Name)]
    public sealed class CloudGatewayNetworkTest : IDisposable
    {
        public CloudGatewayNetworkTest()
        {
            _trap = new TcpListener(IPAddress.Loopback, 0);
            _trap.Start();
            _trap.BeginAcceptTcpClient(_ => Interlocked.Increment(ref _connections), null);
            _settings = new TestSettings();
            _storage = new TempDirectory();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("ControlPlane:Url", "http://control-plane.invalid");
                builder.UseSetting("ControlPlane:GatewayId", Guid.NewGuid().ToString("D"));
                builder.UseSetting("ControlPlane:Token", "configured-token");
                builder.UseSetting("ControlPlane:CamerasFile", System.IO.Path.Combine(_storage.Path, "assigned.json"));
                builder.UseSetting("ControlPlane:EventOutboxFile", System.IO.Path.Combine(_storage.Path, "outbox.jsonl"));
                builder.UseSetting("Gateway:Location", "Cloud");
            });
            _client = TestSettings.Authenticate(_factory.CreateClient());
        }

        public void Dispose()
        {
            _client.Dispose();
            _factory.Dispose();
            _trap.Stop();
            _settings.Dispose();
            _storage.Dispose();
        }

        private readonly TcpListener _trap;
        private int _connections;
        private readonly TestSettings _settings;
        private readonly TempDirectory _storage;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        private int Port => ((IPEndPoint)_trap.LocalEndpoint).Port;

        private async Task<JsonElement> TestAsync(object camera)
        {
            var response = await _client.PostAsJsonAsync("/api/config/cameras/test", camera);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        }

        [Fact]
        public async Task ItShouldNotReachCamerasOnItsOwnNetwork()
        {
            Assert.True(CameraNetwork.IsRestricted);

            // ONVIF (SOAP) and RTSP, by address and by name.
            foreach (var camera in new object[]
            {
                new { onvifUrl = "http://10.0.0.1/onvif/device_service" },
                new { rtspUrl = $"rtsp://127.0.0.1:{Port}/stream" },
                new { rtspUrl = $"rtsp://localhost:{Port}/stream" },
                new { onvifUrl = "http://169.254.169.254/latest/meta-data/" }
            })
            {
                var result = await TestAsync(camera);
                Assert.True("unreachable" == result.GetProperty("kind").GetString(), result.ToString());
                Assert.Contains("not on the internet", result.GetProperty("error").GetString());
            }

            // No looking around the network either.
            Assert.Equal(HttpStatusCode.NotImplemented, (await _client.GetAsync("/api/discovery")).StatusCode);

            await Task.Delay(200);
            Assert.Equal(0, Volatile.Read(ref _connections));
        }

        [Fact]
        public void TheRuleShouldEndWithTheGateway()
        {
            Assert.True(CameraNetwork.IsRestricted);
            _factory.Dispose();
            Assert.False(CameraNetwork.IsRestricted);
        }
    }
}
