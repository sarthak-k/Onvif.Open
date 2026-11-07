// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Core.Operations;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Test.Camera.Fakes;
using Onvif.Open.Test.Camera.Unit.Streaming;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Analytics
{
    public class AnalyticsStreamTest
    {
        #region Private Methods

        private static FakeRtspServer MetadataServer(int documents)
        {
            var server = new FakeRtspServer { IncludeMetadataTrack = true };
            var bytes = Encoding.UTF8.GetBytes(AnalyticsMetadataTest.ProfileMDocument);
            ushort sequence = 1;

            for (var i = 0; i < documents; i++)
            {
                // Each document split over two packets; the marker ends it.
                server.Packets.Add(RtpPacketFactory.Create(sequence++, (uint)(i * 9000), false, bytes.Take(200).ToArray(), 107));
                server.Packets.Add(RtpPacketFactory.Create(sequence++, (uint)(i * 9000), true, bytes.Skip(200).ToArray(), 107));
            }

            return server;
        }

        private static async Task<List<AnalyticsFrame>> ReadAsync(Onvif.Open.Core.Abstract.Interface.Analytics.IAnalyticsStream stream, int count)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var frames = new List<AnalyticsFrame>();

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

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CameraShouldStreamAnalyticsOfTheProfileWithMetadata(bool media2)
        {
            await using (var rtsp = MetadataServer(documents: 3))
            using (var device = new FakeOnvifServer(rtsp.StreamUri) { HasMetadata = true, HasMedia2 = media2 })
            {
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var media = CameraMedia.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());

                await using (var stream = await media.StartAnalyticsStreamAsync())
                {
                    var frames = await ReadAsync(stream, 3);

                    Assert.Equal(3, frames.Count);
                    Assert.All(frames, f => Assert.Equal("Human", f.Objects[0].Class));
                    Assert.Equal(StreamTransport.Rtsp, stream.Transport);
                }

                if (!media2)
                {
                    Assert.Equal("Profile_2", device.LastStreamUriProfile);
                }

                // Only the metadata track is set up.
                Assert.Contains(rtsp.Requests, r => r.StartsWith("SETUP", StringComparison.Ordinal) && r.EndsWith("trackID=3", StringComparison.Ordinal));
                Assert.DoesNotContain(rtsp.Requests, r => r.StartsWith("SETUP", StringComparison.Ordinal) && r.EndsWith("trackID=1", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task CameraWithoutMetadataProfileShouldBeNotSupported()
        {
            using (var device = new FakeOnvifServer(new Uri("rtsp://127.0.0.1:1/stream")))
            {
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var media = CameraMedia.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());

                var error = await Assert.ThrowsAsync<NotSupportedException>(() => media.StartAnalyticsStreamAsync());
                Assert.Contains("metadata configuration", error.Message);
            }
        }

        [Fact]
        public async Task StreamWithoutMetadataTrackShouldBeNotSupported()
        {
            await using (var rtsp = new FakeRtspServer())
            {
                await Assert.ThrowsAsync<NotSupportedException>(() =>
                    Onvif.Open.Streaming.Implementation.Builder.LiveVideoStreamBuilder.GetBuilder()
                        .SetStreamUri(rtsp.StreamUri)
                        .SetCredentials("admin", "12345")
                        .BuildAnalyticsAsync());
            }
        }

        #endregion
    }
}
