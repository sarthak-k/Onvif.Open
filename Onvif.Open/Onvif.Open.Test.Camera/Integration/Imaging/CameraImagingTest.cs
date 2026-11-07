// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Core.Imaging;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Imaging.Implementation.Client;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Imaging
{
    public class CameraImagingTest
    {
        #region Private Methods

        private static FakeOnvifServer Device()
        {
            return new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream"));
        }

        private static ICameraImaging Imaging(FakeOnvifServer device)
        {
            var camera = new TestCameraInformation(device.DeviceServiceUri);
            var deviceManagement = DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build();
            var media = (CameraMedia)CameraMedia.CreateInstance(camera, deviceManagement);
            return CameraImaging.CreateInstance(camera, deviceManagement, media.GetMediaServiceAsync);
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task SettingsShouldBeReadIgnoringUnknownExtensions()
        {
            using (var device = Device())
            {
                device.Imaging["Brightness"] = "62.5";
                device.Imaging["IrCutFilter"] = "AUTO";
                device.Imaging["Wdr"] = "ON";

                var settings = await Imaging(device).GetSettingsAsync();

                Assert.Equal(62.5, settings.Brightness);
                Assert.Equal(50, settings.Contrast);
                Assert.Equal(IrCutFilterMode.Auto, settings.IrCutFilter);
                Assert.False(settings.BacklightCompensation);
                Assert.True(settings.WideDynamicRange);
                Assert.Equal(50, settings.WideDynamicRangeLevel);
                Assert.Equal(ImagingControlMode.Auto, settings.Exposure);
                Assert.Equal(ImagingControlMode.Auto, settings.Focus);
                Assert.Contains("VS_1", device.LastImagingRequest);
            }
        }

        [Fact]
        public async Task UpdateShouldSendOnlyChangedSettingsInSchemaOrder()
        {
            using (var device = Device())
            {
                await Imaging(device).UpdateAsync(new ImagingSettings { IrCutFilter = IrCutFilterMode.Off, Brightness = 70, WideDynamicRange = true });

                var request = device.LastImagingRequest;
                Assert.Contains("SetImagingSettings", request);
                Assert.Contains("VS_1", request);
                Assert.Matches(">true</[\\w:]*ForcePersistence>", request);
                Assert.DoesNotContain("Contrast", request);
                Assert.DoesNotContain("Sharpness", request);
                Assert.DoesNotContain("Exposure", request);
                Assert.DoesNotContain("<Level", request.Replace("tt:", string.Empty));

                // Schema order: Brightness < IrCutFilter < WideDynamicRange.
                Assert.True(request.IndexOf("Brightness", StringComparison.Ordinal) < request.IndexOf("IrCutFilter", StringComparison.Ordinal));
                Assert.True(request.IndexOf("IrCutFilter", StringComparison.Ordinal) < request.IndexOf("WideDynamicRange", StringComparison.Ordinal));

                Assert.Equal("70", device.Imaging["Brightness"]);
                Assert.Equal("OFF", device.Imaging["IrCutFilter"]);
                Assert.Equal("ON", device.Imaging["Wdr"]);
                Assert.Equal("50", device.Imaging["Contrast"]);
            }
        }

        [Fact]
        public async Task EmptyUpdateShouldNotCallTheDevice()
        {
            using (var device = Device())
            {
                await Imaging(device).UpdateAsync(new ImagingSettings());
                Assert.Empty(device.ImagingOperations);
            }
        }

        [Fact]
        public async Task OptionsShouldExposeRangesAndModes()
        {
            using (var device = Device())
            {
                var options = await Imaging(device).GetOptionsAsync();

                Assert.Equal(0, options.Brightness.Min);
                Assert.Equal(100, options.Brightness.Max);
                Assert.Equal(-50, options.Contrast.Min);
                Assert.Equal(15, options.Sharpness.Max);
                Assert.Equal(new[] { IrCutFilterMode.On, IrCutFilterMode.Off, IrCutFilterMode.Auto }, options.IrCutFilterModes);
                Assert.True(options.SupportsBacklightCompensation);
                Assert.True(options.SupportsWideDynamicRange);
                Assert.Equal(100, options.WideDynamicRangeLevel.Max);
                Assert.Equal(new[] { ImagingControlMode.Auto, ImagingControlMode.Manual }, options.ExposureModes);
                Assert.Equal(new[] { ImagingControlMode.Auto }, options.WhiteBalanceModes);
                Assert.Equal(0, options.Contrast.FromFraction(0.5));
                Assert.False(options.ColorSaturation.Contains(101));
            }
        }

        [Fact]
        public async Task FocusShouldMoveStopAndReportStatus()
        {
            using (var device = Device())
            {
                var imaging = Imaging(device);

                await imaging.MoveFocusAsync(0.3, TimeSpan.FromMilliseconds(100));
                Assert.Equal(new[] { "Move", "Stop" }, device.ImagingOperations.ToArray());
                Assert.False(device.FocusMoving);

                await imaging.FocusToAsync(0.8);
                var status = await imaging.GetFocusStatusAsync();
                Assert.Equal(0.8, status.Position.Value, 3);
                Assert.Equal(FocusMoveState.Idle, status.MoveState);

                await imaging.MoveFocusAsync(-1);
                Assert.Equal(FocusMoveState.Moving, (await imaging.GetFocusStatusAsync()).MoveState);
                await imaging.StopFocusAsync();
                Assert.Equal(FocusMoveState.Idle, (await imaging.GetFocusStatusAsync()).MoveState);
            }
        }

        [Fact]
        public async Task CameraWithoutImagingShouldBeNotSupported()
        {
            using (var device = Device())
            {
                device.HasImaging = false;

                await Assert.ThrowsAsync<NotSupportedException>(() => Imaging(device).GetSettingsAsync());
                Assert.Empty(device.ImagingOperations);
            }
        }

        [Fact]
        public void WideDynamicRangeLevelAloneShouldTurnWdrOn()
        {
            var wire = ImagingMapper.ToWire(new ImagingSettings { WideDynamicRangeLevel = 80 });

            Assert.Equal("ON", wire.WideDynamicRange.Mode);
            Assert.True(wire.WideDynamicRange.LevelSpecified);
            Assert.False(wire.BrightnessSpecified);
            Assert.Null(wire.IrCutFilter);
        }

        #endregion
    }
}
