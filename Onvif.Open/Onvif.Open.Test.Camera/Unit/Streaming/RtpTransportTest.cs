// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Onvif.Open.Core.Operations;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Transport;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Streaming
{
    public class RtpTransportTest
    {
        #region Private Methods

        private static byte[] Packet(ushort sequence)
        {
            return RtpPacketFactory.Create(sequence, 0, false, new byte[] { (byte)sequence });
        }

        private static ushort[] Sequences(IEnumerable<byte[]> packets)
        {
            return packets.Select(p =>
            {
                Assert.True(RtpPacket.TryParse(p, out var packet));
                return packet.SequenceNumber;
            }).ToArray();
        }

        #endregion

        #region Public Methods

        [Fact]
        public void FactoryShouldSelectTransportStrategy()
        {
            Assert.IsType<TcpInterleavedRtpTransport>(RtpTransportFactory.Create(StreamTransport.Rtsp, IPAddress.Loopback));

            using (var udp = RtpTransportFactory.Create(StreamTransport.Udp, IPAddress.Loopback))
            {
                Assert.IsType<UdpRtpTransport>(udp);
            }

            Assert.Throws<NotSupportedException>(() => RtpTransportFactory.Create(StreamTransport.Http, IPAddress.Loopback));
        }

        [Fact]
        public void UdpTransportShouldBindEvenOddPortPair()
        {
            using (var udp = new UdpRtpTransport(IPAddress.Loopback))
            {
                Assert.Equal(0, udp.ClientRtpPort % 2);
                Assert.Equal(udp.ClientRtpPort + 1, udp.ClientRtcpPort);
                Assert.Equal($"RTP/AVP;unicast;client_port={udp.ClientRtpPort}-{udp.ClientRtcpPort}", udp.GetRequestTransport(0));
            }
        }

        [Fact]
        public void UdpTransportShouldRejectInterleavedAnswer()
        {
            using (var udp = new UdpRtpTransport(IPAddress.Loopback))
            {
                Assert.Throws<NotSupportedException>(() => udp.Configure(0, "RTP/AVP/TCP;unicast;interleaved=0-1"));
            }
        }

        [Theory]
        [InlineData("RTP/AVP;unicast;client_port=5000-5001;server_port=6970-6971", "server_port", 6970, 6971)]
        [InlineData("RTP/AVP;unicast;client_port=5000", "client_port", 5000, 5001)]
        public void TransportHeaderShouldParseRanges(string header, string name, int first, int second)
        {
            Assert.True(TransportHeader.TryGetRange(header, name, out var a, out var b));
            Assert.Equal(first, a);
            Assert.Equal(second, b);
        }

        [Fact]
        public void ReorderBufferShouldRestoreSequenceOrder()
        {
            var buffer = new RtpReorderBuffer();
            var ready = new List<byte[]>();

            foreach (var sequence in new ushort[] { 10, 12, 11, 14, 13 })
            {
                buffer.Add(Packet(sequence), ready);
            }

            Assert.Equal(new ushort[] { 10, 11, 12, 13, 14 }, Sequences(ready));
        }

        [Fact]
        public void ReorderBufferShouldReorderFirstPackets()
        {
            var buffer = new RtpReorderBuffer();
            var ready = new List<byte[]>();

            foreach (var sequence in new ushort[] { 101, 100, 103, 102, 104 })
            {
                buffer.Add(Packet(sequence), ready);
            }

            Assert.Equal(new ushort[] { 100, 101, 102, 103, 104 }, Sequences(ready));
        }

        [Fact]
        public void ReorderBufferShouldHandleSequenceWrapAround()
        {
            var buffer = new RtpReorderBuffer();
            var ready = new List<byte[]>();

            foreach (var sequence in new ushort[] { 65534, 0, 65535, 1 })
            {
                buffer.Add(Packet(sequence), ready);
            }

            Assert.Equal(new ushort[] { 65534, 65535, 0, 1 }, Sequences(ready));
        }

        [Fact]
        public void ReorderBufferShouldDropDuplicatesAndLatePackets()
        {
            var buffer = new RtpReorderBuffer();
            var ready = new List<byte[]>();

            foreach (var sequence in new ushort[] { 5, 6, 7, 8, 6, 4, 9 })
            {
                buffer.Add(Packet(sequence), ready);
            }

            Assert.Equal(new ushort[] { 5, 6, 7, 8, 9 }, Sequences(ready));
        }

        [Fact]
        public void ReorderBufferShouldSkipLostPacketWhenFull()
        {
            var buffer = new RtpReorderBuffer(capacity: 3);
            var ready = new List<byte[]>();

            // 2 is lost: 3, 4 and 5 wait; 6 overflows the buffer and delivery continues from 3.
            foreach (var sequence in new ushort[] { 1, 3, 4, 5, 6 })
            {
                buffer.Add(Packet(sequence), ready);
            }

            Assert.Equal(new ushort[] { 1, 3, 4, 5, 6 }, Sequences(ready));

            // The lost packet arriving late is discarded.
            buffer.Add(Packet(2), ready);
            Assert.Equal(5, ready.Count);
        }

        #endregion
    }
}
