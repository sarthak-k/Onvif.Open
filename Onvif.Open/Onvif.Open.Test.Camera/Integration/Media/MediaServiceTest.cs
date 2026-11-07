// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Media.Implementation.Builder;
using Onvif.Open.Test.Camera.Fakes;
using Onvif.Open.Test.Camera.Integration.Streaming;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Media
{
    public class MediaServiceTest
    {
        #region Public Methods

        [Fact]
        public async Task ShouldReturnProfilesAndUris()
        {
            using (var device = new FakeOnvifServer(new Uri("rtsp://192.168.1.3:554/Streaming/Channels/101")))
            {
                var media = MediaServiceBuilder.GetBuilder()
                    .SetCamera(new TestCameraInformation(device.DeviceServiceUri))
                    .SetServiceUri(device.MediaServiceUri)
                    .Build();

                var profiles = await media.GetProfilesAsync();

                Assert.Equal(2, profiles.Count);
                Assert.Equal("Profile_1", profiles[0].Token);
                Assert.Equal("mainStream", profiles[0].Name);
                Assert.Equal("H264", profiles[0].VideoEncoding);
                Assert.Equal(1920, profiles[0].Width);
                Assert.Equal(1080, profiles[0].Height);
                Assert.Equal(25, profiles[0].FrameRateLimit);
                Assert.Equal(512, profiles[1].BitrateLimit);

                Assert.Equal(device.RtspUri, media.GetStreamUri(profiles[0].Token));
                Assert.Equal($"http://127.0.0.1:{device.Port}/snapshot.jpg", media.GetSnapshotUri(profiles[0].Token).ToString());

                var streamUriRequest = device.RequestBodies.Single(b => b.Contains("GetStreamUri"));
                Assert.Contains("RTP-Unicast", streamUriRequest);
                Assert.Contains(">RTSP<", streamUriRequest);
                Assert.Contains("Profile_1", streamUriRequest);
                Assert.Contains("PasswordDigest", streamUriRequest);
                Assert.Contains("<wsse:Username>admin</wsse:Username>", streamUriRequest);
            }
        }

        [Fact]
        public void DeviceManagementShouldResolveMediaServiceFromCapabilities()
        {
            using (var device = new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")))
            {
                var deviceManagement = DeviceManagementBuilder.GetBuilder()
                    .SetCamera(new TestCameraInformation(device.DeviceServiceUri))
                    .Build();

                Assert.Equal(device.MediaServiceUri, deviceManagement.GetMediaServiceUri());
            }
        }

        [Fact]
        public async Task CameraMediaShouldStartLiveStreamEndToEnd()
        {
            await using (var rtsp = LiveVideoStreamTest.CreateServer())
            using (var device = new FakeOnvifServer(rtsp.StreamUri))
            {
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var deviceManagement = DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build();
                var cameraMedia = CameraMedia.CreateInstance(camera, deviceManagement);

                await using (var stream = await cameraMedia.StartLiveStreamAsync())
                {
                    Assert.Equal(VideoCodec.H264, stream.Codec);

                    await foreach (var frame in stream.ReadFramesAsync())
                    {
                        Assert.True(frame.IsKeyFrame);
                        break;
                    }
                }

                Assert.Contains(device.RequestBodies, b => b.Contains("Profile_1") && b.Contains("GetStreamUri"));
            }
        }

        [Fact]
        public async Task CameraMediaShouldRequestUdpStreamUriAndStreamOverUdp()
        {
            await using (var rtsp = LiveVideoStreamTest.CreateServer())
            using (var device = new FakeOnvifServer(rtsp.StreamUri))
            {
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var cameraMedia = CameraMedia.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());

                await using (var stream = await cameraMedia.StartLiveStreamAsync("Profile_2", StreamTransport.Udp))
                {
                    Assert.Equal(StreamTransport.Udp, stream.Transport);

                    await foreach (var frame in stream.ReadFramesAsync())
                    {
                        Assert.True(frame.IsKeyFrame);
                        break;
                    }
                }

                Assert.Contains(device.RequestBodies, b => b.Contains("GetStreamUri") && b.Contains("Profile_2") && b.Contains(">UDP<"));
            }
        }

        [Fact]
        public void BuilderShouldRequireCamera()
        {
            Assert.Throws<InvalidOperationException>(() => MediaServiceBuilder.GetBuilder().Build());
            Assert.Throws<ArgumentNullException>(() => MediaServiceBuilder.GetBuilder().SetCamera(null));
        }

        #endregion
    }
}
