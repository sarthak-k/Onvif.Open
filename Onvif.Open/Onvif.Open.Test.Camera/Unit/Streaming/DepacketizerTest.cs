// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Depacketizer;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Sdp;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Streaming
{
    public class DepacketizerTest
    {
        #region Private Variables

        private static readonly byte[] StartCode = { 0, 0, 0, 1 };

        #endregion

        #region Private Methods

        private static RtpPacket Packet(ushort sequence, uint timestamp, bool marker, byte[] payload)
        {
            Assert.True(RtpPacket.TryParse(RtpPacketFactory.Create(sequence, timestamp, marker, payload), out var packet));
            return packet;
        }

        private static byte[] AnnexB(params byte[][] nalUnits)
        {
            return nalUnits.SelectMany(n => StartCode.Concat(n)).ToArray();
        }

        private static IRtpDepacketizer CreateH264()
        {
            var sdp = SessionDescription.Parse(
                "v=0\r\nm=video 0 RTP/AVP 96\r\na=rtpmap:96 H264/90000\r\n" +
                $"a=fmtp:96 sprop-parameter-sets={Convert.ToBase64String(FakeRtspServer.Sps)},{Convert.ToBase64String(FakeRtspServer.Pps)}\r\n");
            return RtpDepacketizerFactory.Create(sdp.Video);
        }

        #endregion

        #region Public Methods

        [Fact]
        public void RtpPacketShouldParseHeader()
        {
            var packet = Packet(42, 9000, true, new byte[] { 1, 2, 3 });

            Assert.True(packet.Marker);
            Assert.Equal(96, packet.PayloadType);
            Assert.Equal(42, packet.SequenceNumber);
            Assert.Equal(9000u, packet.Timestamp);
            Assert.Equal(new byte[] { 1, 2, 3 }, packet.Payload.ToArray());
        }

        [Fact]
        public void RtpPacketShouldRejectInvalidVersion()
        {
            var data = RtpPacketFactory.Create(1, 1, false, new byte[] { 1 });
            data[0] = 0x40;
            Assert.False(RtpPacket.TryParse(data, out _));
        }

        [Fact]
        public void FactoryShouldSelectDepacketizerFromEncoding()
        {
            Assert.IsType<H264Depacketizer>(CreateH264());
            Assert.IsType<H265Depacketizer>(RtpDepacketizerFactory.Create(SessionDescription.Parse("m=video 0 RTP/AVP 98\r\na=rtpmap:98 H265/90000\r\n").Video));
            Assert.IsType<GenericDepacketizer>(RtpDepacketizerFactory.Create(SessionDescription.Parse("m=video 0 RTP/AVP 26\r\na=rtpmap:26 JPEG/90000\r\n").Video));
        }

        [Fact]
        public void H264FragmentedKeyFrameShouldBeReassembledWithParameterSets()
        {
            var depacketizer = CreateH264();
            var idr = new byte[] { 0x65 }.Concat(Enumerable.Range(0, 3000).Select(i => (byte)i)).ToArray();
            var fragments = RtpPacketFactory.FragmentH264(idr, 1000);
            var frames = new List<VideoFrame>();

            for (var i = 0; i < fragments.Count; i++)
            {
                depacketizer.Process(Packet((ushort)i, 1000, i == fragments.Count - 1, fragments[i]), frames);
            }

            var frame = Assert.Single(frames);
            Assert.Equal(VideoCodec.H264, frame.Codec);
            Assert.True(frame.IsKeyFrame);
            Assert.Equal(AnnexB(FakeRtspServer.Sps, FakeRtspServer.Pps, idr), frame.Data.ToArray());
            Assert.Equal(TimeSpan.Zero, frame.Timestamp);
        }

        [Fact]
        public void H264StapAShouldBeSplitIntoNalUnits()
        {
            var depacketizer = CreateH264();
            var slice = new byte[] { 0x65, 0xAA, 0xBB };
            var stapA = new byte[] { 0x18 }
                .Concat(new byte[] { 0, (byte)FakeRtspServer.Sps.Length }).Concat(FakeRtspServer.Sps)
                .Concat(new byte[] { 0, (byte)FakeRtspServer.Pps.Length }).Concat(FakeRtspServer.Pps)
                .Concat(new byte[] { 0, (byte)slice.Length }).Concat(slice)
                .ToArray();
            var frames = new List<VideoFrame>();

            depacketizer.Process(Packet(1, 0, true, stapA), frames);

            // Parameter sets are present in-band, so they must not be duplicated.
            Assert.Equal(AnnexB(FakeRtspServer.Sps, FakeRtspServer.Pps, slice), Assert.Single(frames).Data.ToArray());
        }

        [Fact]
        public void H264ShouldEmitFramesOnTimestampChangeAndComputeTime()
        {
            var depacketizer = CreateH264();
            var frames = new List<VideoFrame>();

            depacketizer.Process(Packet(1, 90000, false, new byte[] { 0x41, 0x01 }), frames);
            depacketizer.Process(Packet(2, 93000, false, new byte[] { 0x41, 0x02 }), frames);
            depacketizer.Process(Packet(3, 96000, true, new byte[] { 0x41, 0x03 }), frames);

            Assert.Equal(3, frames.Count);
            Assert.All(frames, f => Assert.False(f.IsKeyFrame));
            Assert.Equal(AnnexB(new byte[] { 0x41, 0x01 }), frames[0].Data.ToArray());
            Assert.Equal(TimeSpan.FromSeconds(3000.0 / 90000), frames[1].Timestamp);
            Assert.Equal(TimeSpan.FromSeconds(6000.0 / 90000), frames[2].Timestamp);
        }

        [Fact]
        public void H264FragmentWithLostPacketShouldBeDropped()
        {
            var depacketizer = CreateH264();
            var nal = new byte[] { 0x41 }.Concat(Enumerable.Repeat((byte)7, 30)).ToArray();
            var fragments = RtpPacketFactory.FragmentH264(nal, 10);
            var frames = new List<VideoFrame>();

            depacketizer.Process(Packet(1, 0, false, fragments[0]), frames);
            // fragments[1] (sequence 2) lost
            depacketizer.Process(Packet(3, 0, true, fragments[2]), frames);

            Assert.Empty(frames);
        }

        [Fact]
        public void H265FragmentationUnitShouldBeReassembled()
        {
            var depacketizer = RtpDepacketizerFactory.Create(SessionDescription.Parse("m=video 0 RTP/AVP 98\r\na=rtpmap:98 H265/90000\r\n").Video);

            // IDR_W_RADL (type 19): header 0x26 0x01
            var nal = new byte[] { 0x26, 0x01, 0xAA, 0xBB, 0xCC, 0xDD };
            var payloadHeader = new byte[] { (byte)((nal[0] & 0x81) | (49 << 1)), nal[1] };
            var first = payloadHeader.Concat(new byte[] { 0x80 | 19, 0xAA, 0xBB }).ToArray();
            var last = payloadHeader.Concat(new byte[] { 0x40 | 19, 0xCC, 0xDD }).ToArray();
            var frames = new List<VideoFrame>();

            depacketizer.Process(Packet(1, 0, false, first), frames);
            depacketizer.Process(Packet(2, 0, true, last), frames);

            var frame = Assert.Single(frames);
            Assert.Equal(VideoCodec.H265, frame.Codec);
            Assert.True(frame.IsKeyFrame);
            Assert.Equal(AnnexB(nal), frame.Data.ToArray());
        }

        [Fact]
        public void LargeAccessUnitsShouldBeReassembledIntact()
        {
            // 300 KB: larger than the depacketizer's initial buffer, so it has to grow mid-frame.
            var depacketizer = CreateH264();
            var nal = new byte[300_000];
            new Random(3).NextBytes(nal);
            nal[0] = 0x41;
            var fragments = RtpPacketFactory.FragmentH264(nal, 1400);
            var frames = new List<VideoFrame>();

            for (var i = 0; i < fragments.Count; i++)
            {
                depacketizer.Process(Packet((ushort)i, 0, i == fragments.Count - 1, fragments[i]), frames);
            }

            Assert.Equal(AnnexB(nal), Assert.Single(frames).Data.ToArray());
        }

        [Fact]
        public void LostFragmentShouldNotCorruptTheRestOfTheAccessUnit()
        {
            var depacketizer = CreateH264();
            var first = new byte[] { 0x41, 1, 1, 1 };
            var broken = new byte[] { 0x41 }.Concat(Enumerable.Repeat((byte)2, 30)).ToArray();
            var last = new byte[] { 0x41, 3, 3, 3 };
            var fragments = RtpPacketFactory.FragmentH264(broken, 10);
            var frames = new List<VideoFrame>();

            depacketizer.Process(Packet(1, 0, false, first), frames);
            depacketizer.Process(Packet(2, 0, false, fragments[0]), frames);
            // fragments[1] (sequence 3) lost: the partial NAL unit is dropped, the units around it are kept.
            depacketizer.Process(Packet(4, 0, false, fragments[2]), frames);
            depacketizer.Process(Packet(5, 0, true, last), frames);

            Assert.Equal(AnnexB(first, last), Assert.Single(frames).Data.ToArray());
        }

        [Fact]
        public void InBandParameterSetsShouldReplaceTheSdpOnesAndNotBeDuplicated()
        {
            var depacketizer = CreateH264();
            var sps = new byte[] { 0x67, 0x64, 0x00, 0x28 };
            var idr = new byte[] { 0x65, 0x88, 0x84 };
            var frames = new List<VideoFrame>();

            // A key frame carrying its own SPS: only the PPS from the SDP is added, in front.
            depacketizer.Process(Packet(1, 0, false, sps), frames);
            depacketizer.Process(Packet(2, 0, true, idr), frames);

            // The next key frame without parameter sets gets the new SPS.
            depacketizer.Process(Packet(3, 3600, true, idr), frames);

            Assert.Equal(2, frames.Count);
            Assert.Equal(AnnexB(FakeRtspServer.Pps, sps, idr), frames[0].Data.ToArray());
            Assert.Equal(AnnexB(sps, FakeRtspServer.Pps, idr), frames[1].Data.ToArray());
            Assert.All(frames, f => Assert.True(f.IsKeyFrame));
        }

        #endregion
    }
}
