// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Builder;
using Onvif.Open.Test.Camera.Fakes;
using Onvif.Open.Test.Camera.Unit.Streaming;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Streaming
{
    public class AudioStreamTest
    {
        #region Private Methods

        private static async Task<List<MediaFrame>> ReadAsync(ILiveVideoStream stream, int count)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var frames = new List<MediaFrame>();

                await foreach (var frame in stream.ReadMediaFramesAsync(timeout.Token))
                {
                    frames.Add(frame);

                    if (frames.Count == count)
                    {
                        break;
                    }
                }

                return frames;
            }
        }

        private static Task<ILiveVideoStream> Start(FakeRtspServer server, bool audio = true, StreamTransport transport = StreamTransport.Rtsp)
        {
            return LiveVideoStreamBuilder.GetBuilder()
                .SetStreamUri(server.StreamUri)
                .SetCredentials("admin", "12345")
                .SetTransport(transport)
                .SetAudio(audio)
                .BuildAsync();
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task AacAudioShouldArriveWithTheVideo()
        {
            await using (var server = LiveVideoStreamTest.CreateServer())
            {
                server.AudioSdp = AudioDepacketizerTest.AacSdp;
                server.AudioPackets.Add(RtpPacketFactory.Create(1, 32000, true, AudioDepacketizerTest.AacPayload(new byte[] { 1, 2, 3 }, new byte[] { 4, 5 }), 97));
                server.AudioPackets.Add(RtpPacketFactory.Create(2, 34048, true, AudioDepacketizerTest.AacPayload(new byte[] { 6 }), 97));

                await using (var stream = await Start(server))
                {
                    Assert.Equal(AudioCodec.Aac, stream.Audio.Codec);
                    Assert.Equal(16000, stream.Audio.SampleRate);

                    var frames = await ReadAsync(stream, 5);
                    var audio = frames.OfType<AudioFrame>().ToList();

                    Assert.Equal(2, frames.OfType<VideoFrame>().Count());
                    Assert.Equal(3, audio.Count);
                    Assert.Equal(new byte[] { 1, 2, 3 }, audio[0].Data.ToArray());
                    Assert.All(audio, a => Assert.True(a.Timestamp >= TimeSpan.Zero));
                    Assert.True(audio[2].Timestamp > audio[0].Timestamp);
                }

                // Video and audio set up in one session, on their own interleaved channels.
                var setups = server.RequestHeaders.Where(h => h.StartsWith("SETUP Transport", StringComparison.Ordinal)).ToList();
                Assert.Equal(new[] { "SETUP Transport: RTP/AVP/TCP;unicast;interleaved=0-1", "SETUP Transport: RTP/AVP/TCP;unicast;interleaved=2-3" }, setups);
                Assert.Equal(2, setups.Count);
                Assert.True(server.RequestHeaders.Count(h => h == "SETUP Session: 12345678") == 1, "the second SETUP joins the session");
            }
        }

        [Fact]
        public async Task G711AudioShouldBeDecodable()
        {
            await using (var server = LiveVideoStreamTest.CreateServer())
            {
                server.AudioPackets.Add(RtpPacketFactory.Create(1, 0, false, new byte[] { 0xFF, 0x80, 0x00 }, 0));

                await using (var stream = await Start(server))
                {
                    Assert.Equal(AudioCodec.Pcmu, stream.Audio.Codec);

                    var audio = (await ReadAsync(stream, 3)).OfType<AudioFrame>().Single();
                    Assert.Equal(new short[] { 0, 32124, -32124 }, G711.DecodeToPcm16(audio));
                }
            }
        }

        [Fact]
        public async Task WithoutAudioOnlyTheVideoTrackShouldBeSetUp()
        {
            await using (var server = LiveVideoStreamTest.CreateServer())
            {
                server.AudioPackets.Add(RtpPacketFactory.Create(1, 0, false, new byte[] { 0xFF }, 0));

                await using (var stream = await Start(server, audio: false))
                {
                    Assert.Null(stream.Audio);
                    var frames = await ReadAsync(stream, 2);
                    Assert.All(frames, f => Assert.IsType<VideoFrame>(f));
                }

                Assert.Single(server.Requests, r => r.StartsWith("SETUP", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task UnsupportedAudioCodecsShouldStillPlayTheVideo()
        {
            await using (var server = LiveVideoStreamTest.CreateServer())
            {
                server.AudioSdp = "m=audio 0 RTP/AVP 97\r\na=rtpmap:97 G726-32/8000\r\na=control:trackID=2\r\n";

                await using (var stream = await Start(server))
                {
                    Assert.Null(stream.Audio);
                    Assert.Equal(2, (await ReadAsync(stream, 2)).OfType<VideoFrame>().Count());
                }
            }
        }

        [Fact]
        public async Task AudioOverUdpShouldBeRejected()
        {
            await using (var server = LiveVideoStreamTest.CreateServer())
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => Start(server, transport: StreamTransport.Udp));
            }
        }

        #endregion
    }
}
