// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Core.Implementation.Security;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Media.Implementation.Builder;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Media
{
    public class ClockSynchronizationTest
    {
        #region Private Methods

        private static FakeOnvifServer SkewedDevice(TimeSpan offset)
        {
            return new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")) { ClockOffset = offset, RequireFreshTokens = true };
        }

        #endregion

        #region Public Methods

        [Fact]
        public void DeviceManagementShouldMeasureClockOffsetWithoutCredentials()
        {
            using (var device = SkewedDevice(TimeSpan.FromMinutes(-42)))
            {
                var deviceManagement = DeviceManagementBuilder.GetBuilder().SetCamera(new TestCameraInformation(device.DeviceServiceUri)).Build();

                var offset = deviceManagement.GetClockOffset();

                Assert.NotNull(offset);
                Assert.InRange(offset.Value.TotalSeconds, -42 * 60 - 2, -42 * 60 + 2);

                var request = device.RequestBodies.Single();
                Assert.Contains("GetSystemDateAndTime", request);
                Assert.DoesNotContain("UsernameToken", request);
            }
        }

        [Fact]
        public void SkewedDeviceShouldRejectUnsynchronizedTokens()
        {
            using (var device = SkewedDevice(TimeSpan.FromHours(3)))
            {
                var media = MediaServiceBuilder.GetBuilder().SetCamera(new TestCameraInformation(device.DeviceServiceUri)).SetServiceUri(device.MediaServiceUri).Build();

                var error = Assert.ThrowsAny<CommunicationException>(() => media.GetProfiles());

                Assert.True(OnvifFaults.IsAuthenticationFailure(error), error.ToString());
                Assert.Equal(1, device.RejectedRequests);
            }
        }

        [Fact]
        public async Task CameraMediaShouldSynchronizeBeforeTheFirstSecuredCall()
        {
            using (var device = SkewedDevice(TimeSpan.FromHours(3)))
            {
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var cameraMedia = CameraMedia.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());

                var jpeg = await cameraMedia.GetSnapshotAsync();

                Assert.Equal(FakeOnvifServer.SnapshotJpeg, jpeg);
                Assert.Equal(0, device.RejectedRequests);
                Assert.InRange(DeviceClock.For(camera).Offset.TotalHours, 2.99, 3.01);
                Assert.NotNull(DeviceClock.For(camera).LastSynchronized);
            }
        }

        [Fact]
        public async Task CameraMediaShouldResynchronizeAndRetryWhenTheDeviceClockJumps()
        {
            using (var device = SkewedDevice(TimeSpan.Zero))
            {
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var cameraMedia = CameraMedia.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());

                await cameraMedia.GetSnapshotAsync();

                // The camera's clock is reset (e.g. after a power cut without NTP) while we are connected.
                device.ClockOffset = TimeSpan.FromDays(-400);
                var jpeg = await cameraMedia.GetSnapshotAsync();

                Assert.Equal(FakeOnvifServer.SnapshotJpeg, jpeg);
                Assert.Equal(1, device.RejectedRequests);
                Assert.InRange(DeviceClock.For(camera).Offset.TotalDays, -400.01, -399.99);
            }
        }

        [Fact]
        public async Task WrongPasswordShouldFailWithoutRetrying()
        {
            using (var device = SkewedDevice(TimeSpan.Zero))
            {
                device.RequireFreshTokens = false;
                var camera = new TestCameraInformation(device.DeviceServiceUri, "admin", "wrong");
                var cameraMedia = CameraMedia.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());
                device.RejectAllTokens = true;

                await Assert.ThrowsAnyAsync<CommunicationException>(() => cameraMedia.GetSnapshotAsync());

                // One rejected attempt; the clock did not move so there is no pointless retry.
                Assert.Equal(1, device.RejectedRequests);
            }
        }

        [Fact]
        public void CameraShouldExposeSynchronizeClock()
        {
            using (var device = SkewedDevice(TimeSpan.FromSeconds(90)))
            {
                var url = device.DeviceServiceUri;

                // CameraBuilder rejects loopback addresses, so the façade is exercised through its clock directly.
                var camera = new TestCameraInformation(url);
                var synchronizer = new ClockSynchronizer(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());

                Assert.InRange(synchronizer.Synchronize().TotalSeconds, 88, 92);
                Assert.InRange(synchronizer.Offset.TotalSeconds, 88, 92);
            }
        }

        [Fact]
        public void UnreachableDeviceShouldNotBreakEnsureSynchronized()
        {
            var camera = new TestCameraInformation(new Uri("http://127.0.0.1:1/onvif/device_service"));
            var synchronizer = new ClockSynchronizer(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());

            synchronizer.EnsureSynchronized();

            Assert.Equal(TimeSpan.Zero, synchronizer.Offset);
            Assert.Null(DeviceClock.For(camera).LastSynchronized);
        }

        #endregion
    }
}
