// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class MpegTsAudioTest
    {
        #region Private Methods

        internal static List<byte[]> Packets(byte[] ts)
        {
            Assert.Equal(0, ts.Length % 188);
            return Enumerable.Range(0, ts.Length / 188).Select(i => ts.Skip(i * 188).Take(188).ToArray()).ToList();
        }

        internal static int Pid(byte[] packet) => ((packet[1] & 0x1F) << 8) | packet[2];

        private static byte[] Mux(AudioFormat audio, Action<ISegmentMuxer> write)
        {
            using (var output = new MemoryStream())
            {
                using (var muxer = SegmentFormats.MpegTs.CreateMuxer(output, VideoCodec.H264, audio))
                {
                    write(muxer);
                }

                return output.ToArray();
            }
        }

        #endregion

        #region Public Methods

        [Fact]
        public void AacShouldBeMuxedAsAdtsOnItsOwnPid()
        {
            var ts = Mux(FakeLiveVideoStream.Aac16k, muxer =>
            {
                // Audio before the first video frame is dropped: it is not on the segment's timeline yet.
                muxer.Write(FakeLiveVideoStream.AudioAt(FakeLiveVideoStream.Aac16k, 0));
                muxer.Write(FakeLiveVideoStream.Frame(0, true));
                muxer.Write(FakeLiveVideoStream.AudioAt(FakeLiveVideoStream.Aac16k, 10, size: 300));
                muxer.Write(FakeLiveVideoStream.Frame(1, false));
            });

            var packets = Packets(ts);
            var audio = packets.Where(p => Pid(p) == 0x101).ToList();

            // 300 bytes + 7 ADTS + 14 PES header = 321 bytes: two transport packets.
            Assert.Equal(2, audio.Count);
            Assert.True((audio[0][1] & 0x40) != 0, "payload unit start");

            var payloadStart = 4 + ((audio[0][3] & 0x20) != 0 ? audio[0][4] + 1 : 0);
            var pes = audio[0].Skip(payloadStart).ToArray();
            Assert.Equal(new byte[] { 0, 0, 1, 0xC0 }, pes.Take(4).ToArray());
            Assert.Equal(8 + 7 + 300, (pes[4] << 8) | pes[5]);

            // ADTS: sync word, AAC-LC (profile 1), 16 kHz (index 8), mono, frame length 307.
            var adts = pes.Skip(14).Take(7).ToArray();
            Assert.Equal(0xFF, adts[0]);
            Assert.Equal(0xF1, adts[1]);
            Assert.Equal((1 << 6) | (8 << 2), adts[2]);
            Assert.Equal(307, ((adts[3] & 0x03) << 11) | (adts[4] << 3) | (adts[5] >> 5));

            // The PMT lists video and audio (stream type 0x0F) and its CRC is valid.
            var pmt = packets.First(p => Pid(p) == 0x1000);
            var sectionLength = ((pmt[6] & 0x0F) << 8) | pmt[7];
            Assert.Equal(23, sectionLength);
            Assert.Equal(0x0F, pmt[5 + 17]);
            Assert.Equal(0u, MpegTsMuxer.Crc32(pmt.AsSpan(5, 3 + sectionLength)));
        }

        [Fact]
        public void G711AndMuxersWithoutAudioShouldWriteVideoOnly()
        {
            var g711 = new AudioFormat(AudioCodec.Pcmu, 8000, 1);
            Assert.False(SegmentFormats.MpegTs.SupportsAudio(g711));
            Assert.True(SegmentFormats.MpegTs.SupportsAudio(FakeLiveVideoStream.Aac16k));
            Assert.False(SegmentFormats.ElementaryStream.SupportsAudio(FakeLiveVideoStream.Aac16k));

            var ts = Mux(g711, muxer =>
            {
                muxer.Write(FakeLiveVideoStream.Frame(0, true));
                muxer.Write(FakeLiveVideoStream.AudioAt(g711, 10));
            });

            Assert.DoesNotContain(Packets(ts), p => Pid(p) == 0x101);
            var pmt = Packets(ts).First(p => Pid(p) == 0x1000);
            Assert.Equal(18, ((pmt[6] & 0x0F) << 8) | pmt[7]);
        }

        [Theory]
        [InlineData(new byte[] { 0x14, 0x08 }, 16000, 1, true)]     // AAC-LC 16 kHz mono
        [InlineData(new byte[] { 0x12, 0x10 }, 44100, 2, true)]     // AAC-LC 44.1 kHz stereo
        [InlineData(new byte[0], 48000, 2, true)]                   // no config: LC from rate and channels
        [InlineData(new byte[] { 0x2B, 0x8A }, 16000, 1, false)]    // HE-AAC (object type 5): not expressible in ADTS
        [InlineData(new byte[0], 17000, 1, false)]                  // non-standard rate
        public void AdtsShouldOnlyBeUsedForExpressibleConfigurations(byte[] config, int rate, int channels, bool supported)
        {
            Assert.Equal(supported, SegmentFormats.MpegTs.SupportsAudio(new AudioFormat(AudioCodec.Aac, rate, channels, config)));
        }

        #endregion
    }
}
