// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Core.Ptz;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Ptz.Implementation.Client;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Ptz
{
    public class CameraPtzTest
    {
        #region Private Methods

        private static FakeOnvifServer Device()
        {
            return new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream"));
        }

        private static ICameraPtz Ptz(FakeOnvifServer device)
        {
            var camera = new TestCameraInformation(device.DeviceServiceUri);
            var deviceManagement = DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build();
            var media = (CameraMedia)CameraMedia.CreateInstance(camera, deviceManagement);
            return CameraPtz.CreateInstance(camera, deviceManagement, media.GetMediaServiceAsync);
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task TimedMoveShouldSendVelocityTimeoutAndStop()
        {
            using (var device = Device())
            {
                var ptz = Ptz(device);

                await ptz.MoveAsync(0.5, -0.25, 0, TimeSpan.FromMilliseconds(200));

                Assert.Equal(new[] { "ContinuousMove", "Stop" }, device.PtzOperations.Where(o => o != "GetStatus"));
                Assert.False(device.PtzMoving);

                var move = device.RequestBodies.First(b => b.Contains("ContinuousMove"));
                Assert.Contains("Profile_2", move);                  // the profile with a PTZ configuration
                Assert.Matches("PanTilt[^>]*x=\"0.5\"", move);
                Assert.Matches("PanTilt[^>]*y=\"-0.25\"", move);
                Assert.DoesNotContain("Zoom", move);                 // axes that do not move are left out
                Assert.Contains("PT0.2S", move);
            }
        }

        [Fact]
        public async Task UntimedMoveShouldKeepMovingUntilStopped()
        {
            using (var device = Device())
            {
                var ptz = Ptz(device);

                await ptz.MoveAsync(0, 0, 1);
                Assert.True(device.PtzMoving);
                Assert.True((await ptz.GetStatusAsync()).IsMoving);
                Assert.Matches("Zoom[^>]*x=\"1\"", device.LastPtzRequest.Contains("ContinuousMove") ? device.LastPtzRequest : device.RequestBodies.Last(b => b.Contains("ContinuousMove")));

                await ptz.MoveAsync(0, 0, 0);
                Assert.False(device.PtzMoving);
                Assert.Equal(PtzMoveState.Idle, (await ptz.GetStatusAsync()).PanTilt);
            }
        }

        [Fact]
        public async Task CancelledTimedMoveShouldStillStop()
        {
            using (var device = Device())
            using (var cancel = new CancellationTokenSource())
            {
                var ptz = Ptz(device);
                var move = ptz.MoveAsync(1, 0, 0, TimeSpan.FromSeconds(30), cancel.Token);

                // Cancel while the camera is moving (not before the move was even sent).
                var waited = System.Diagnostics.Stopwatch.StartNew();
                while (!device.PtzMoving && waited.Elapsed < TimeSpan.FromSeconds(10))
                {
                    await Task.Delay(10);
                }

                Assert.True(device.PtzMoving);
                cancel.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => move);

                Assert.False(device.PtzMoving);
                Assert.Equal("Stop", device.PtzOperations.Last());
            }
        }

        [Fact]
        public async Task AbsoluteAndRelativeMovesShouldUpdatePosition()
        {
            using (var device = Device())
            {
                var ptz = Ptz(device);

                await ptz.MoveToAsync(new PtzVector(0.5, 0.25, 0.75));
                await ptz.MoveByAsync(new PtzVector(pan: -0.25));

                var status = await ptz.GetStatusAsync();
                Assert.Equal(0.25, status.Position.Pan.Value, 3);
                Assert.Equal(0.25, status.Position.Tilt.Value, 3);
                Assert.Equal(0.75, status.Position.Zoom.Value, 3);
                Assert.Equal(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero), status.UtcTime);
            }
        }

        [Fact]
        public async Task PresetsShouldBeSavedRecalledAndRemovedByNameOrToken()
        {
            using (var device = Device())
            {
                var ptz = Ptz(device);

                await ptz.MoveToAsync(new PtzVector(0.1, 0.2, 0.3));
                var entrance = await ptz.SavePresetAsync("Entrance");
                await ptz.MoveToAsync(new PtzVector(-0.5, -0.5, 0));
                var parking = await ptz.SavePresetAsync("Parking");

                // Saving under an existing name overwrites that preset.
                await ptz.MoveToAsync(new PtzVector(-0.6, -0.6, 0));
                Assert.Equal(parking, await ptz.SavePresetAsync("parking"));
                Assert.Equal(2, (await ptz.GetPresetsAsync()).Count);

                await ptz.GoToPresetAsync("entrance");
                Assert.Equal((0.1, 0.2, 0.3), (Math.Round(device.PtzPan, 3), Math.Round(device.PtzTilt, 3), Math.Round(device.PtzZoom, 3)));

                await ptz.GoToPresetAsync(parking);
                Assert.Equal(-0.6, device.PtzPan, 3);

                Assert.True(await ptz.RemovePresetAsync("Entrance"));
                Assert.False(await ptz.RemovePresetAsync(entrance));
                await Assert.ThrowsAsync<KeyNotFoundException>(() => ptz.GoToPresetAsync("Entrance"));

                var remaining = Assert.Single(await ptz.GetPresetsAsync());
                Assert.Equal("Parking", remaining.Name);
                Assert.Equal(-0.6, remaining.Position.Pan.Value, 3);
            }
        }

        [Fact]
        public async Task HomePositionShouldBeSetAndRecalled()
        {
            using (var device = Device())
            {
                var ptz = Ptz(device);

                await ptz.MoveToAsync(new PtzVector(0.3, 0.3, 0.3));
                await ptz.SetHomeAsync();
                await ptz.MoveToAsync(new PtzVector(-1, -1, 0));
                await ptz.GoHomeAsync();

                Assert.Equal(0.3, device.PtzPan, 3);
                Assert.Equal(0.3, device.PtzZoom, 3);
            }
        }

        [Fact]
        public async Task CameraWithoutPtzShouldBeNotSupported()
        {
            using (var device = Device())
            {
                device.HasPtz = false;

                await Assert.ThrowsAsync<NotSupportedException>(() => Ptz(device).MoveAsync(1, 0, 0));
                Assert.Empty(device.PtzOperations);
            }
        }

        [Fact]
        public void VectorsShouldOmitMissingAxes()
        {
            Assert.Null(PtzService.ToVector(null));
            Assert.Null(PtzService.ToVector(new PtzVector()));

            var zoomOnly = PtzService.ToVector(new PtzVector(zoom: 0.5));
            Assert.Null(zoomOnly.PanTilt);
            Assert.Equal(0.5f, zoomOnly.Zoom.X);

            var panOnly = PtzService.ToVector(new PtzVector(pan: 0.25));
            Assert.Equal(0.25f, panOnly.PanTilt.X);
            Assert.Equal(0f, panOnly.PanTilt.Y);
            Assert.Null(panOnly.Zoom);

            Assert.Null(PtzService.FromVector(null));
        }

        #endregion
    }
}
