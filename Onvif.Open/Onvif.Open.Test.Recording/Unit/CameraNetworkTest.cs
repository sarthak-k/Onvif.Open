// Sarthak Killedar
// 2026-11-11

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Networking;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    /// <summary>Which addresses a gateway in the cloud may connect to cameras at.</summary>
    public sealed class CameraNetworkTest
    {
        [Theory]
        [InlineData("8.8.8.8", true)]
        [InlineData("1.1.1.1", true)]
        [InlineData("100.63.255.255", true)]
        [InlineData("100.128.0.1", true)]
        [InlineData("172.15.255.255", true)]
        [InlineData("172.32.0.1", true)]
        [InlineData("2606:4700:4700::1111", true)]
        [InlineData("::ffff:8.8.8.8", true)]
        [InlineData("64:ff9b::808:808", true)]
        [InlineData("0.0.0.0", false)]
        [InlineData("10.1.2.3", false)]
        [InlineData("100.64.0.1", false)]
        [InlineData("100.127.255.255", false)]
        [InlineData("127.0.0.1", false)]
        [InlineData("169.254.169.254", false)]
        [InlineData("172.16.0.1", false)]
        [InlineData("172.31.255.255", false)]
        [InlineData("192.0.0.170", false)]
        [InlineData("192.0.2.1", false)]
        [InlineData("192.168.1.10", false)]
        [InlineData("198.18.0.1", false)]
        [InlineData("198.51.100.7", false)]
        [InlineData("203.0.113.9", false)]
        [InlineData("224.0.0.251", false)]
        [InlineData("239.255.255.250", false)]
        [InlineData("255.255.255.255", false)]
        [InlineData("::", false)]
        [InlineData("::1", false)]
        [InlineData("fe80::1", false)]
        [InlineData("fec0::1", false)]
        [InlineData("fc00::1", false)]
        [InlineData("fd12:3456::1", false)]
        [InlineData("ff02::1", false)]
        [InlineData("2001:db8::1", false)]
        [InlineData("::ffff:127.0.0.1", false)]
        [InlineData("::ffff:169.254.169.254", false)]
        [InlineData("64:ff9b::a9fe:a9fe", false)]
        [InlineData("2002:a9fe:a9fe::1", false)]
        [InlineData("::127.0.0.1", false)]
        [InlineData("100::1", false)]
        public void OnlyInternetAddressesShouldBePublic(string address, bool expected)
        {
            Assert.Equal(expected, CameraNetwork.IsPublic(IPAddress.Parse(address)));
        }

        [Fact]
        public async Task NamesShouldBeJudgedByWhatTheyResolveTo()
        {
            Assert.Equal(new[] { IPAddress.Parse("8.8.8.8") }, await CameraNetwork.ResolveAsync("8.8.8.8", CameraNetwork.PublicOnly, CancellationToken.None));
            await Assert.ThrowsAsync<CameraAddressRefusedException>(() => CameraNetwork.ResolveAsync("localhost", CameraNetwork.PublicOnly, CancellationToken.None));
            await Assert.ThrowsAsync<CameraAddressRefusedException>(() => CameraNetwork.ResolveAsync("[::1]", CameraNetwork.PublicOnly, CancellationToken.None));
            await Assert.ThrowsAsync<CameraAddressRefusedException>(() => CameraNetwork.EnsureAllowedAsync("rtsp://169.254.169.254/latest/meta-data", CameraNetwork.PublicOnly, CancellationToken.None));
            await CameraNetwork.EnsureAllowedAsync("rtsp://8.8.8.8/stream", CameraNetwork.PublicOnly, CancellationToken.None);

            // At a site, anything goes.
            Assert.NotEmpty(await CameraNetwork.ResolveAsync("localhost", CameraNetwork.Any, CancellationToken.None));
        }

        [Fact]
        public async Task ConnectionsShouldOnlyBeOpenedWhereTheRuleAllows()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                using (var socket = await CameraNetwork.ConnectAsync("127.0.0.1", port, CameraNetwork.Any, CancellationToken.None))
                using (await listener.AcceptTcpClientAsync())
                {
                    Assert.True(socket.Connected);
                }

                await Assert.ThrowsAsync<CameraAddressRefusedException>(() => CameraNetwork.ConnectAsync("localhost", port, CameraNetwork.PublicOnly, CancellationToken.None));

                // HTTP (snapshots, webhooks) too.
                using (var http = new HttpClient(CameraNetwork.CreateHttpHandler(CameraNetwork.PublicOnly)))
                {
                    var error = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetAsync($"http://127.0.0.1:{port}/snapshot.jpg"));
                    Assert.IsType<CameraAddressRefusedException>(error.InnerException);
                }

                Assert.False(listener.Pending());
            }
            finally
            {
                listener.Stop();
            }
        }
    }
}
