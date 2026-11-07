// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Media.Implementation.Builder;
using Onvif.Open.Media.Implementation.Client;
using Onvif.Open.Test.Camera.Fakes;
using Onvif.Open.Test.Camera.Integration.Streaming;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Media
{
    public class Media2Test
    {
        #region Private Methods

        private static FakeOnvifServer Device(bool hasMedia2 = true)
        {
            return new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")) { HasMedia2 = hasMedia2 };
        }

        private static Onvif.Open.Core.Abstract.Interface.Device.IDeviceManagement DeviceManagement(FakeOnvifServer device, out TestCameraInformation camera)
        {
            camera = new TestCameraInformation(device.DeviceServiceUri);
            return DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build();
        }

        #endregion

        #region Public Methods

        [Fact]
        public void GetServicesShouldLocateMedia2()
        {
            using (var withMedia2 = Device())
            using (var withoutMedia2 = Device(hasMedia2: false))
            using (var old = Device())
            {
                old.SupportsGetServices = false;

                Assert.Equal(withMedia2.Media2ServiceUri, DeviceManagement(withMedia2, out _).FindServiceUri(OnvifServiceNamespaces.Media2));
                Assert.Equal(withMedia2.PtzServiceUri, DeviceManagement(withMedia2, out _).FindServiceUri(OnvifServiceNamespaces.Ptz));
                Assert.Null(DeviceManagement(withoutMedia2, out _).FindServiceUri(OnvifServiceNamespaces.Media2));
                Assert.Null(DeviceManagement(old, out _).FindServiceUri(OnvifServiceNamespaces.Media2));
            }
        }

        [Fact]
        public async Task Media2ServiceShouldMapH265ProfilesAndProtocols()
        {
            using (var device = Device())
            {
                var media = MediaServiceBuilder.GetBuilder()
                    .SetCamera(new TestCameraInformation(device.DeviceServiceUri))
                    .SetVersion(MediaServiceVersion.Media2)
                    .SetServiceUri(device.Media2ServiceUri)
                    .Build();

                Assert.Equal(MediaServiceVersion.Media2, media.Version);

                var profiles = await media.GetProfilesAsync();
                Assert.Equal(new[] { "M2_Main", "M2_Sub" }, profiles.Select(p => p.Token));
                Assert.Equal("H265", profiles[0].VideoEncoding);
                Assert.Equal(3840, profiles[0].Width);
                Assert.Equal(30, profiles[0].FrameRateLimit);   // 29.97 rounded
                Assert.Equal(12, profiles[1].FrameRateLimit);   // 12.5 rounds to even
                Assert.Null(profiles[0].PtzConfigurationToken);
                Assert.Equal("PTZ_2", profiles[1].PtzConfigurationToken);

                Assert.Equal(device.RtspUri, await media.GetStreamUriAsync("M2_Main"));
                Assert.Equal("RTSP", device.LastMedia2Protocol);
                media.GetStreamUri("M2_Main", StreamTransport.Udp);
                Assert.Equal("RtspUnicast", device.LastMedia2Protocol);
                media.GetStreamUri("M2_Main", StreamTransport.Http);
                Assert.Equal("RtspOverHttp", device.LastMedia2Protocol);

                Assert.Equal(device.SnapshotUri, media.GetSnapshotUri("M2_Sub"));
                Assert.Contains(device.RequestBodies, b => b.Contains("GetProfiles") && b.Contains(">All<"));
            }
        }

        [Fact]
        public void AutoShouldPreferMedia2AndFallBackToMedia1()
        {
            using (var withMedia2 = Device())
            using (var withoutMedia2 = Device(hasMedia2: false))
            {
                var dm2 = DeviceManagement(withMedia2, out var camera2);
                var dm1 = DeviceManagement(withoutMedia2, out var camera1);

                Assert.Equal(MediaServiceVersion.Media2, CameraMedia.ResolveMediaService(camera2, dm2, MediaServiceVersion.Auto).Version);
                Assert.Equal(MediaServiceVersion.Media1, CameraMedia.ResolveMediaService(camera1, dm1, MediaServiceVersion.Auto).Version);
                Assert.Equal(withoutMedia2.MediaServiceUri, CameraMedia.ResolveMediaService(camera1, dm1, MediaServiceVersion.Auto).ServiceUri);

                // Forcing Media1 ignores Media2; forcing Media2 on a device without it is an error.
                Assert.Equal(MediaServiceVersion.Media1, CameraMedia.ResolveMediaService(camera2, dm2, MediaServiceVersion.Media1).Version);
                Assert.Throws<NotSupportedException>(() => CameraMedia.ResolveMediaService(camera1, dm1, MediaServiceVersion.Media2));
            }
        }

        [Fact]
        public async Task CameraMediaShouldStreamThroughMedia2()
        {
            await using (var rtsp = LiveVideoStreamTest.CreateServer())
            using (var device = new FakeOnvifServer(rtsp.StreamUri) { HasMedia2 = true })
            {
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var cameraMedia = CameraMedia.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());

                await using (var stream = await cameraMedia.StartLiveStreamAsync())
                {
                    Assert.Equal(VideoCodec.H264, stream.Codec);

                    await foreach (var frame in stream.ReadFramesAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token))
                    {
                        Assert.True(frame.IsKeyFrame);
                        break;
                    }
                }

                Assert.Contains(device.RequestBodies, b => b.Contains("ver20/media/wsdl") && b.Contains("GetStreamUri") && b.Contains("M2_Main"));
                Assert.DoesNotContain(device.RequestBodies, b => b.Contains("ver10/media/wsdl") && b.Contains("GetStreamUri"));
            }
        }

        [Fact]
        public void Media2ProtocolNamesShouldMatchTheSpecification()
        {
            Assert.Equal("RTSP", Media2Service.ToProtocol(StreamTransport.Rtsp));
            Assert.Equal("RtspUnicast", Media2Service.ToProtocol(StreamTransport.Udp));
            Assert.Equal("RtspOverHttp", Media2Service.ToProtocol(StreamTransport.Http));
        }

        [Fact]
        public void BuilderShouldRejectAuto()
        {
            Assert.Throws<ArgumentException>(() => MediaServiceBuilder.GetBuilder().SetVersion(MediaServiceVersion.Auto));
        }

        #endregion
    }
}
