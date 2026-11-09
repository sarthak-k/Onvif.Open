// Sarthak Killedar
// 2026-11-07

using System;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    public class DiscoveryApiTest
    {
        #region Private Methods

        /// <summary>Answers every probe with one ProbeMatch for a camera at 192.168.1.10.</summary>
        private static async Task AnswerProbesAsync(UdpClient socket, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult probe;
                try
                {
                    probe = await socket.ReceiveAsync(cancellationToken);
                }
                catch (Exception)
                {
                    return;
                }

                var messageId = Regex.Match(Encoding.UTF8.GetString(probe.Buffer), "<a:MessageID>([^<]+)<").Groups[1].Value;
                var answer = Encoding.UTF8.GetBytes(
                    "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\">" +
                    $"<s:Header><a:RelatesTo>{messageId}</a:RelatesTo></s:Header><s:Body><d:ProbeMatches><d:ProbeMatch>" +
                    "<a:EndpointReference><a:Address>urn:uuid:front-door</a:Address></a:EndpointReference>" +
                    "<d:Scopes>onvif://www.onvif.org/name/Front onvif://www.onvif.org/hardware/IPC-1 onvif://www.onvif.org/Profile/Streaming</d:Scopes>" +
                    "<d:XAddrs>http://192.168.1.10/onvif/device_service</d:XAddrs>" +
                    "</d:ProbeMatch></d:ProbeMatches></s:Body></s:Envelope>");

                await socket.SendAsync(answer, answer.Length, probe.RemoteEndPoint);
            }
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task DiscoveryShouldListDevicesAndFlagConfiguredOnes()
        {
            using (var storage = new TempDirectory())
            using (var settings = new TestSettings().Camera(new Onvif.Open.Recorder.Configuration.CameraOptions { Id = "front", Enabled = false, OnvifUrl = "http://192.168.1.10" }))
            using (var device = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var stop = new CancellationTokenSource())
            {
                var port = ((IPEndPoint)device.Client.LocalEndPoint).Port;
                var responder = AnswerProbesAsync(device, stop.Token);

                using (var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
                {
                    settings.Apply(builder);
                    builder.UseSetting("Recording:StorageDirectory", storage.Path);
                    builder.UseSetting("Discovery:Timeout", "00:00:00.500");
                }))
                using (var client = TestSettings.Authenticate(factory.CreateClient()))
                {
                    // Where the gateway looks is a gateway setting, applied at once.
                    var changed = await client.PutAsJsonAsync($"/api/gateways/{TestSettings.GatewayId}", new { discovery = new { multicast = false, addresses = new[] { $"127.0.0.1:{port}" } } });
                    Assert.Equal(HttpStatusCode.OK, changed.StatusCode);

                    var devices = JsonDocument.Parse(await client.GetStringAsync("/api/discovery")).RootElement;

                    var found = Assert.Single(devices.EnumerateArray());
                    Assert.Equal("Front", found.GetProperty("name").GetString());
                    Assert.Equal("IPC-1", found.GetProperty("hardware").GetString());
                    Assert.Equal("http://192.168.1.10/onvif/device_service", found.GetProperty("deviceServiceUri").GetString());
                    Assert.Equal("Streaming", found.GetProperty("profiles")[0].GetString());
                    Assert.Equal("front", found.GetProperty("configuredCameraId").GetString());
                }

                stop.Cancel();
                await responder;
            }
        }

        #endregion
    }
}
