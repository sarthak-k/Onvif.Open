// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class MpegTsMuxerTest
    {
        #region Private Methods

        private static byte[] Mux(VideoCodec codec, params VideoFrame[] frames)
        {
            using (var output = new MemoryStream())
            {
                using (var muxer = SegmentFormats.MpegTs.CreateMuxer(output, codec))
                {
                    foreach (var frame in frames)
                    {
                        muxer.Write(frame);
                    }
                }

                return output.ToArray();
            }
        }

        private static IEnumerable<byte[]> Packets(byte[] ts)
        {
            for (var i = 0; i < ts.Length; i += MpegTsMuxer.PacketSize)
            {
                yield return ts.AsSpan(i, MpegTsMuxer.PacketSize).ToArray();
            }
        }

        private static int Pid(byte[] packet) => ((packet[1] & 0x1F) << 8) | packet[2];

        private static ReadOnlySpan<byte> Payload(byte[] packet)
        {
            var afc = (packet[3] >> 4) & 0x3;
            var offset = 4 + (afc == 3 ? 1 + packet[4] : 0);
            return packet.AsSpan(offset);
        }

        /// <summary>Reassembles the PES packets of the video PID.</summary>
        private static List<byte[]> VideoPes(byte[] ts)
        {
            var result = new List<byte[]>();
            List<byte> current = null;

            foreach (var packet in Packets(ts).Where(p => Pid(p) == MpegTsMuxer.VideoPid))
            {
                if ((packet[1] & 0x40) != 0)
                {
                    if (current != null)
                    {
                        result.Add(current.ToArray());
                    }

                    current = new List<byte>();
                }

                current.AddRange(Payload(packet).ToArray());
            }

            if (current != null)
            {
                result.Add(current.ToArray());
            }

            return result;
        }

        private static long Pts(byte[] pes)
        {
            return ((long)(pes[9] & 0x0E) << 29) | ((long)pes[10] << 22) | ((long)(pes[11] & 0xFE) << 14) | ((long)pes[12] << 7) | ((long)pes[13] >> 1);
        }

        #endregion

        #region Public Methods

        [Fact]
        public void Crc32ShouldMatchMpeg2CheckValue()
        {
            Assert.Equal(0x0376E6E7u, MpegTsMuxer.Crc32(Encoding.ASCII.GetBytes("123456789")));
        }

        [Fact]
        public void OutputShouldBeAlignedTransportStreamPackets()
        {
            var ts = Mux(VideoCodec.H264, FakeLiveVideoStream.Frame(0, true, 1000), FakeLiveVideoStream.Frame(1, false, 50));

            Assert.Equal(0, ts.Length % MpegTsMuxer.PacketSize);
            Assert.All(Packets(ts), p => Assert.Equal(0x47, p[0]));
        }

        [Fact]
        public void PatAndPmtShouldHaveValidCrcAndDescribeH264()
        {
            var packets = Packets(Mux(VideoCodec.H264, FakeLiveVideoStream.Frame(0, true))).ToList();

            var pat = packets.First(p => Pid(p) == 0);
            var patSection = pat.AsSpan(5, 3 + 13);
            Assert.Equal(0u, MpegTsMuxer.Crc32(patSection));

            var pmt = packets.First(p => Pid(p) == MpegTsMuxer.PmtPid);
            var pmtSection = pmt.AsSpan(5, 3 + 18);
            Assert.Equal(0u, MpegTsMuxer.Crc32(pmtSection));
            Assert.Equal(0x1B, pmtSection[12]);

            var hevcPmt = Packets(Mux(VideoCodec.H265, FakeLiveVideoStream.Frame(0, true))).First(p => Pid(p) == MpegTsMuxer.PmtPid);
            Assert.Equal(0x24, hevcPmt[5 + 12]);
        }

        [Fact]
        public void PesShouldCarryAccessUnitWithDelimiterAndPts()
        {
            var first = FakeLiveVideoStream.Frame(0, true, 500);
            var second = FakeLiveVideoStream.Frame(1, false, 20);

            var pes = VideoPes(Mux(VideoCodec.H264, first, second));

            Assert.Equal(2, pes.Count);
            Assert.Equal(new byte[] { 0, 0, 1, 0xE0 }, pes[0].Take(4));
            Assert.Equal(MpegTsMuxer.PtsOffset, Pts(pes[0]));
            Assert.Equal(MpegTsMuxer.PtsOffset + 3600, Pts(pes[1]));

            var expected = new byte[] { 0, 0, 0, 1, 0x09, 0xF0 }.Concat(first.Data.ToArray()).ToArray();
            Assert.Equal(expected, pes[0].Skip(14).ToArray());
        }

        [Fact]
        public void KeyFrameShouldSetRandomAccessAndPcr()
        {
            var videoStarts = Packets(Mux(VideoCodec.H264, FakeLiveVideoStream.Frame(0, true), FakeLiveVideoStream.Frame(1, false)))
                .Where(p => Pid(p) == MpegTsMuxer.VideoPid && (p[1] & 0x40) != 0)
                .ToList();

            Assert.Equal(0x50, videoStarts[0][5]); // random_access_indicator + PCR_flag
            Assert.Equal(0x10, videoStarts[1][5]); // PCR only
        }

        [Fact]
        public void UnsupportedCodecShouldThrow()
        {
            Assert.False(SegmentFormats.MpegTs.Supports(VideoCodec.Unknown));
            Assert.Throws<NotSupportedException>(() => SegmentFormats.MpegTs.CreateMuxer(new MemoryStream(), VideoCodec.Unknown));
        }

        [Fact]
        public void ElementaryStreamShouldWriteRawAnnexB()
        {
            var frame = FakeLiveVideoStream.Frame(0, true);

            using (var output = new MemoryStream())
            {
                using (var muxer = SegmentFormats.ElementaryStream.CreateMuxer(output, VideoCodec.H264))
                {
                    muxer.Write(frame);
                }

                Assert.Equal(frame.Data.ToArray(), output.ToArray());
                Assert.Equal(".h264", SegmentFormats.ElementaryStream.GetFileExtension(VideoCodec.H264));
                Assert.Equal(".h265", SegmentFormats.ElementaryStream.GetFileExtension(VideoCodec.H265));
            }
        }

        #endregion
    }
}
