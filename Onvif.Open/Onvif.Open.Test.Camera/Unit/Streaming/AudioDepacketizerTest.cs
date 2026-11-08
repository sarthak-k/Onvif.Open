// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Depacketizer.Audio;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Rtsp;
using Onvif.Open.Streaming.Implementation.Sdp;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Streaming
{
    public class AudioDepacketizerTest
    {
        #region Private Variables

        internal const string AacSdp = "m=audio 0 RTP/AVP 97\r\na=rtpmap:97 mpeg4-generic/16000/1\r\na=fmtp:97 streamtype=5;profile-level-id=15;mode=AAC-hbr;config=1408;sizelength=13;indexlength=3;indexdeltalength=3\r\na=control:trackID=2\r\n";

        #endregion

        #region Private Methods

        private static SdpMediaDescription Audio(string media)
        {
            return SessionDescription.Parse("v=0\r\ns=x\r\nt=0 0\r\n" + media).Audio;
        }

        private static RtpPacket Packet(ushort sequence, uint timestamp, bool marker, byte[] payload, byte payloadType = 97)
        {
            Assert.True(RtpPacket.TryParse(RtpPacketFactory.Create(sequence, timestamp, marker, payload, payloadType), out var packet));
            return packet;
        }

        /// <summary>RFC 3640 AAC-hbr payload: 16-bit AU-headers-length, 13-bit size + 3-bit index per AU, then the AUs.</summary>
        internal static byte[] AacPayload(params byte[][] accessUnits)
        {
            var headers = new List<byte>();
            foreach (var unit in accessUnits)
            {
                var header = (ushort)(unit.Length << 3);
                headers.Add((byte)(header >> 8));
                headers.Add((byte)header);
            }

            var bits = accessUnits.Length * 16;
            return new[] { (byte)(bits >> 8), (byte)bits }.Concat(headers).Concat(accessUnits.SelectMany(u => u)).ToArray();
        }

        #endregion

        #region Public Methods

        [Fact]
        public void AacTrackShouldBeRecognizedWithItsConfig()
        {
            var depacketizer = AudioDepacketizerFactory.Create(Audio(AacSdp));

            Assert.IsType<AacDepacketizer>(depacketizer);
            Assert.Equal(AudioCodec.Aac, depacketizer.Format.Codec);
            Assert.Equal(16000, depacketizer.Format.SampleRate);
            Assert.Equal(1, depacketizer.Format.Channels);
            Assert.Equal(new byte[] { 0x14, 0x08 }, depacketizer.Format.Config.ToArray());
        }

        [Theory]
        [InlineData("m=audio 0 RTP/AVP 0\r\n", AudioCodec.Pcmu)]
        [InlineData("m=audio 0 RTP/AVP 8\r\n", AudioCodec.Pcma)]
        [InlineData("m=audio 0 RTP/AVP 96\r\na=rtpmap:96 PCMA/8000\r\n", AudioCodec.Pcma)]
        public void G711TracksShouldBeRecognizedAlsoWithoutRtpmap(string media, AudioCodec codec)
        {
            var depacketizer = AudioDepacketizerFactory.Create(Audio(media));

            Assert.Equal(codec, depacketizer.Format.Codec);
            Assert.Equal(8000, depacketizer.Format.SampleRate);
        }

        [Theory]
        [InlineData("m=audio 0 RTP/AVP 97\r\na=rtpmap:97 G726-32/8000\r\n")]
        [InlineData("m=audio 0 RTP/AVP 97\r\na=rtpmap:97 MP4A-LATM/16000\r\n")]
        public void UnsupportedAudioShouldBeIgnored(string media)
        {
            Assert.Null(AudioDepacketizerFactory.Create(Audio(media)));
        }

        [Fact]
        public void AacPacketsWithSeveralAccessUnitsShouldBeSplit()
        {
            var depacketizer = AudioDepacketizerFactory.Create(Audio(AacSdp));
            var frames = new List<AudioFrame>();

            depacketizer.Process(Packet(1, 16000, true, AacPayload(new byte[] { 1, 2, 3 }, new byte[] { 4, 5 })), frames);
            depacketizer.Process(Packet(2, 18048, true, AacPayload(new byte[] { 6 })), frames);

            Assert.Equal(3, frames.Count);
            Assert.Equal(new byte[] { 1, 2, 3 }, frames[0].Data.ToArray());
            Assert.Equal(new byte[] { 4, 5 }, frames[1].Data.ToArray());
            Assert.Equal(TimeSpan.Zero, frames[0].Timestamp);
            Assert.Equal(TimeSpan.FromMilliseconds(64), frames[1].Timestamp);   // 1024 samples at 16 kHz
            Assert.Equal(TimeSpan.FromMilliseconds(128), frames[2].Timestamp);
        }

        [Fact]
        public void FragmentedAacAccessUnitsShouldBeReassembled()
        {
            var depacketizer = AudioDepacketizerFactory.Create(Audio(AacSdp));
            var frames = new List<AudioFrame>();
            var unit = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();

            // Every fragment carries the AU header with the full size; the marker ends the access unit.
            byte[] Fragment(byte[] data) => new byte[] { 0x00, 0x10, (byte)(unit.Length >> 5), (byte)(unit.Length << 3) }.Concat(data).ToArray();

            depacketizer.Process(Packet(1, 1000, false, Fragment(unit.Take(200).ToArray())), frames);
            Assert.Empty(frames);
            depacketizer.Process(Packet(2, 1000, true, Fragment(unit.Skip(200).ToArray())), frames);

            Assert.Equal(unit, Assert.Single(frames).Data.ToArray());
        }

        [Fact]
        public void G711ShouldDecodeToLinearPcm()
        {
            Assert.Equal(0, G711.DecodeMuLaw(0xFF));
            Assert.Equal(-32124, G711.DecodeMuLaw(0x00));
            Assert.Equal(32124, G711.DecodeMuLaw(0x80));
            Assert.Equal(8, G711.DecodeALaw(0xD5));
            Assert.Equal(-8, G711.DecodeALaw(0x55));
            Assert.Equal(32256, G711.DecodeALaw(0xAA));

            var frame = new AudioFrame(new AudioFormat(AudioCodec.Pcmu, 8000, 1), new byte[] { 0xFF, 0x80 }, 0, TimeSpan.Zero);
            Assert.Equal(new short[] { 0, 32124 }, G711.DecodeToPcm16(frame));
            Assert.Throws<NotSupportedException>(() => G711.DecodeToPcm16(new AudioFrame(new AudioFormat(AudioCodec.Aac, 16000, 1), new byte[1], 0, TimeSpan.Zero)));
        }

        [Fact]
        public void AudioShouldBeAlignedToTheVideoTimelineByCaptureTime()
        {
            var sdp = SessionDescription.Parse("v=0\r\ns=x\r\nt=0 0\r\nm=video 0 RTP/AVP 96\r\na=rtpmap:96 H264/90000\r\na=control:trackID=1\r\n" + AacSdp);
            var reader = new MediaTrackReader(includeAudio: true);
            var frames = new List<MediaFrame>();
            var start = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

            Assert.Equal(2, reader.SelectTracks(sdp).Count);

            // Audio before the first video packet has no timeline yet.
            reader.Process(1, Parse(RtpPacketFactory.CreateReplay(1, 500, true, AacPayload(new byte[] { 9 }), start.AddMilliseconds(-100))), frames);
            Assert.Empty(frames);

            reader.Process(0, Parse(RtpPacketFactory.CreateReplay(1, 9000, true, new byte[] { 0x65, 0x88 }, start, cleanPoint: true)), frames);

            // 4000 samples (250 ms) after the first audio packet, which was 100 ms before the first video packet.
            reader.Process(1, Parse(RtpPacketFactory.CreateReplay(2, 4500, true, AacPayload(new byte[] { 7 }), start.AddMilliseconds(150))), frames);

            var audio = frames.OfType<AudioFrame>().Single();
            // NTP fractions round to a tick or so.
            Assert.InRange(audio.Timestamp.TotalMilliseconds, 149, 151);
            Assert.InRange((audio.CaptureTime.Value - start).TotalMilliseconds, 149, 151);
            Assert.Equal(AudioCodec.Aac, reader.Audio.Codec);
        }

        #endregion

        private static RtpPacket Parse(byte[] data)
        {
            Assert.True(RtpPacket.TryParse(data, out var packet));
            return packet;
        }
    }
}
