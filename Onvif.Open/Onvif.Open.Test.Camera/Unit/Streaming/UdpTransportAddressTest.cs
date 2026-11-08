// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Streaming.Implementation.Transport;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Streaming
{
    public class UdpTransportAddressTest
    {
        #region Public Methods

        /// <summary>
        /// On hosts with IPv6, TcpClient uses a dual-stack socket and reports the RTSP server as an IPv4-mapped
        /// IPv6 address. The RTP socket must still receive the device's IPv4 datagrams.
        /// </summary>
        [Theory]
        [InlineData("::ffff:127.0.0.1")]
        [InlineData("127.0.0.1")]
        public async Task ShouldReceiveIPv4DatagramsWhateverTheServerAddressFormat(string serverAddress)
        {
            await using (var rtsp = await SilentRtspConnection.CreateAsync())
            using (var transport = new UdpRtpTransport(IPAddress.Parse(serverAddress)))
            using (var device = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                transport.Configure(0, $"RTP/AVP;unicast;client_port={transport.ClientRtpPort}-{transport.ClientRtcpPort};server_port=1-2");
                var received = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                var packets = new List<byte[]>();

                var receiving = transport.ReceiveAsync(rtsp.Connection, (track, packet) =>
                {
                    packets.Add(packet);
                    if (packets.Count == 4)
                    {
                        received.TrySetResult(packet);
                    }

                    return default;
                }, timeout.Token);

                for (ushort sequence = 0; sequence < 4; sequence++)
                {
                    var rtp = RtpPacketFactory.Create(sequence, 0, false, new byte[] { 0x41 });
                    await device.SendAsync(rtp, rtp.Length, new IPEndPoint(IPAddress.Loopback, transport.ClientRtpPort));
                }

                await received.Task.WaitAsync(timeout.Token);
                Assert.Equal(4, packets.Count);

                timeout.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiving);
            }
        }

        #endregion
    }
}
