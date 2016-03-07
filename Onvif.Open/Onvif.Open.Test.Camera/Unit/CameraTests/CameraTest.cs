// Sarthak Killedar
// 2016-03-07 @ 9:08 PM

using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.CameraTests
{
    public class CameraTest
    {
        #region Constructor

        public CameraTest()
        {
            _cameraBuilder = CameraBuilder.GetBuilder();
            _expectedCameraUrl = CameraUrl + "/onvif/device_service";
        }

        #endregion

        #region Private Variables

        private readonly CameraBuilder _cameraBuilder;

        private readonly string _expectedCameraUrl;

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
        public void CameraBuilderShouldReturnCameraObjectAfterBuilding()
        {
            var camera = _cameraBuilder.SetUrl(CameraUrl)
                .SetUsername("admin")
                .SetPassword("12345")
                .Build();

            Assert.IsAssignableFrom<ICamera>(camera);
        }

        [Fact]
        public void CameraShouldReturnICameraInformation()
        {
            var camera = _cameraBuilder.SetUrl(CameraUrl)
                .SetUsername("admin")
                .SetPassword("12345")
                .Build();

            var cameraInformation = camera.GetCameraInformation();

            Assert.IsAssignableFrom<ICameraInformation>(cameraInformation);
        }

        [Fact]
        public void CameraShouldReturnICameraDeviceManagement()
        {
            var camera = _cameraBuilder.SetUrl(CameraUrl)
                .SetUsername(Username)
                .SetPassword(Password)
                .Build();

            var cameraDeviceManagement = camera.Initialize();

            Assert.IsAssignableFrom<ICameraDeviceManagement>(cameraDeviceManagement);
        }

        [Fact]
        public void CameraUrlShouldMatch()
        {
            var camera = _cameraBuilder.SetUrl(CameraUrl)
                .SetUsername(Username)
                .SetPassword(Password)
                .Build();

            var cameraInformation = camera.GetCameraInformation();

            Assert.Equal(_expectedCameraUrl, cameraInformation.CameraUri.ToString());
        }

        [Fact]
        public void CameraPasswordShouldMatch()
        {
            var camera = _cameraBuilder.SetUrl(CameraUrl)
                .SetUsername(Username)
                .SetPassword(Password)
                .Build();

            var cameraInformation = camera.GetCameraInformation();

            Assert.Equal(Password, cameraInformation.Password);
        }

        [Fact]
        public void CameraUsernameShouldMatch()
        {
            var camera = _cameraBuilder.SetUrl(CameraUrl)
                .SetUsername(Username)
                .SetPassword(Password)
                .Build();

            var cameraInformation = camera.GetCameraInformation();

            Assert.Equal(Username, cameraInformation.Username);
        }

        #endregion

    }
}