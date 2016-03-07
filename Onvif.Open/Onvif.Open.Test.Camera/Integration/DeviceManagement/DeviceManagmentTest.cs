// Sarthak Killedar
// 2016-03-07 @ 10:08 PM

using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.DeviceManagement
{
    public class DeviceManagmentTest
    {
        #region Constructor

        public DeviceManagmentTest()
        {
            _cameraBuilder = CameraBuilder.GetBuilder();
            _camera = _cameraBuilder.SetUrl(CameraUrl).SetUsername(Username).SetPassword(Password).Build();
            _cameraDeviceManagement = _camera.Initialize();
        }
        #endregion

        #region Private Variables

        private readonly CameraBuilder _cameraBuilder;

        private readonly ICamera _camera;

        private readonly ICameraDeviceManagement _cameraDeviceManagement;

        private static string Password => "12345";

        private static string Username => "admin";

        private static string CameraUrl => "http://192.168.1.1";

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        [Fact]
        public void CameraDeviceManagementShouldNotBeNull()
        {
            Assert.NotNull(_cameraDeviceManagement);
        }

        [Fact]
        public void CameraDeviceManagementShouldReturnDeviceManagement()
        {
            var deviceManagement = _cameraDeviceManagement.GetDeviceManagement();
            Assert.IsAssignableFrom<IDeviceManagement>(deviceManagement);
        }

        #endregion

    }
}