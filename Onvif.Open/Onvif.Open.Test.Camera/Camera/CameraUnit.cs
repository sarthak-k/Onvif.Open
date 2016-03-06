using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Test.Camera.Camera
{
    [TestClass]
    public class CameraUnit
    {
        #region Constructor

        public CameraUnit()
        {
            _camera =
                CameraBuilder.GetBuilder()
                    .SetUrl(CameraUrl)
                    .SetUsername(Username)
                    .SetPassword(Password)
                    .Build();

            _cameraInformation = _camera.GetCameraInformation();
        }

        #endregion

        #region Private Variables

        private readonly ICamera _camera;
        private readonly ICameraInformation _cameraInformation;

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        private static string Username => "admin";
        private static string Password => "12345";
        private static string CameraUrl => "http://192.168.1.15/doc/page/main.asp";
        #endregion

        #region Public Methods

        [TestMethod]
        public void IsCamera()
        {
            Assert.IsInstanceOfType(_camera,typeof(ICamera));
        }

        [TestMethod]
        public void IsCameraUsername()
        {
            Assert.AreEqual(_cameraInformation.Username, Username);
        }

        [TestMethod]
        public void IsCameraPassword()
        {
            Assert.AreEqual(_cameraInformation.Password, Password);
        }

        [TestMethod]
        public void IsCameraUrl()
        {
            const string expectedUrl = "http://192.168.1.15/onvif/device_service";
            Assert.AreEqual(_cameraInformation.CameraUri, expectedUrl);
        }

        #endregion
    }
}
