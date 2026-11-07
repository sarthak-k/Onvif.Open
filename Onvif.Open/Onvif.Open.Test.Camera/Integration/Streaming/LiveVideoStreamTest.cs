// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Builder;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Streaming
{
    public class LiveVideoStreamTest
    {
        #region Private Variables

        private static readonly byte[] Idr = new byte[] { 0x65 }.Concat(Enumerable.Range(0, 2500).Select(i => (byte)i)).ToArray();
        private static readonly byte[] Slice = { 0x41, 0x9A, 0x02 };

        #endregion

        #region Private Methods

        internal static FakeRtspServer CreateServer()
        {
            var server = new FakeRtspServer();
            ushort sequence = 100;

            foreach (var fragment in RtpPacketFactory.FragmentH264(Idr, 1000).Select((data, index) => new { data, index }))
            {
                server.Packets.Add(RtpPacketFactory.Create(sequence++, 3000, fragment.index == 2, fragment.data));
            }

            server.Packets.Add(RtpPacketFactory.Create(sequence, 6000, true, Slice));
            return server;
        }

        private static async Task<List<VideoFrame>> ReadAsync(ILiveVideoStream stream, int count)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var frames = new List<VideoFrame>();
                await foreach (var frame in stream.ReadFramesAsync(timeout.Token))
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

        #endregion

        #region Public Methods

        [Fact]
        public async Task ShouldAuthenticateAndReceiveFrames()
        {
            await using (var server = CreateServer())
            {
                var stream = await LiveVideoStreamBuilder.GetBuilder()
                    .SetStreamUri(server.StreamUri)
                    .SetCredentials("admin", "12345")
                    .BuildAsync();

                await using (stream)
                {
                    Assert.Equal(VideoCodec.H264, stream.Codec);
                    Assert.Contains("H264/90000", stream.SessionDescription);

                    var frames = await ReadAsync(stream, 2);

                    Assert.True(frames[0].IsKeyFrame);
                    var expectedKeyFrame = new byte[] { 0, 0, 0, 1 }.Concat(FakeRtspServer.Sps)
                        .Concat(new byte[] { 0, 0, 0, 1 }).Concat(FakeRtspServer.Pps)
                        .Concat(new byte[] { 0, 0, 0, 1 }).Concat(Idr);
                    Assert.Equal(expectedKeyFrame, frames[0].Data.ToArray());

                    Assert.False(frames[1].IsKeyFrame);
                    Assert.Equal(new byte[] { 0, 0, 0, 1 }.Concat(Slice), frames[1].Data.ToArray());
                    Assert.Equal(TimeSpan.FromSeconds(3000.0 / 90000), frames[1].Timestamp);
                }

                await server.TeardownReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

                var requests = server.Requests.ToArray();
                Assert.Contains($"SETUP {server.StreamUri}/trackID=1", requests);
                Assert.Contains(requests, r => r.StartsWith("PLAY ", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task ShouldUseCredentialsEmbeddedInStreamUri()
        {
            await using (var server = CreateServer())
            {
                var uri = new UriBuilder(server.StreamUri) { UserName = "admin", Password = "12345" }.Uri;

                await using (var stream = await LiveVideoStreamBuilder.GetBuilder().SetStreamUri(uri).BuildAsync())
                {
                    Assert.Equal(2, (await ReadAsync(stream, 2)).Count);
                }

                // Credentials must never be sent in the request line.
                Assert.DoesNotContain(server.Requests, r => r.Contains("admin", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task CopyToShouldWriteElementaryStream()
        {
            await using (var server = CreateServer())
            {
                var stream = await LiveVideoStreamBuilder.GetBuilder().SetStreamUri(server.StreamUri).SetCredentials("admin", "12345").BuildAsync();

                using (var output = new MemoryStream())
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var copy = stream.CopyToAsync(output, timeout.Token);

                    // The fake server stops sending after its packets: stop once both frames were written.
                    while (output.Length < Idr.Length + Slice.Length)
                    {
                        await Task.Delay(20, timeout.Token);
                    }

                    await stream.DisposeAsync();
                    await copy;

                    Assert.Equal(4 + FakeRtspServer.Sps.Length + 4 + FakeRtspServer.Pps.Length + 4 + Idr.Length + 4 + Slice.Length, output.Length);
                }
            }
        }

        [Fact]
        public async Task WrongPasswordShouldThrowUnauthorized()
        {
            await using (var server = CreateServer())
            {
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    LiveVideoStreamBuilder.GetBuilder().SetStreamUri(server.StreamUri).SetCredentials("admin", "wrong").BuildAsync());
            }
        }

        [Fact]
        public async Task BuilderShouldValidateInput()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => LiveVideoStreamBuilder.GetBuilder().BuildAsync());
            Assert.Throws<UriFormatException>(() => LiveVideoStreamBuilder.GetBuilder().SetStreamUri(new Uri("http://192.168.1.3/stream")));
            Assert.Throws<ArgumentNullException>(() => LiveVideoStreamBuilder.GetBuilder().SetCredentials(string.Empty, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => LiveVideoStreamBuilder.GetBuilder().SetTimeout(TimeSpan.Zero));
        }

        #endregion
    }
}
