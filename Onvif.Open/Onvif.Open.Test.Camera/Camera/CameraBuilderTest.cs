using System;
using Onvif.Open.Camera.Implementation;
using Xunit;

namespace Onvif.Open.Test.Camera.Camera
{
    public class CameraBuilderTest
    {
        #region Constructor

        public CameraBuilderTest()
        {
            _cameraBuilder = CameraBuilder.GetBuilder();
        }
        #endregion

        #region Private Variables

        private readonly CameraBuilder _cameraBuilder;
        
        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods


        [Fact]
        public void GetBuilderTest()
        {
            Assert.IsType<CameraBuilder>(_cameraBuilder);
        }

        [Fact]
        public void CameraBuilderShouldThrowErrorWhenNoUrlIsSet()
        {
            Assert.Throws<InvalidOperationException>(() => _cameraBuilder.Build());
        }

        [Fact]
        public void CameraBuilderShouldNotThrowErrorWhenNoUsernameAndPasswordIsSet()
        {
            _cameraBuilder.SetUrl("http://192.168.1.3");
            var exceptionShouldBeNull = Record.Exception(() => _cameraBuilder.Build());
            Assert.Null(exceptionShouldBeNull);
        }

        [Fact]
        public void CameraBuilderShouldThrowErrorWhenEmptyUsernameIsSet()
        {
            _cameraBuilder.SetUrl("http://192.168.1.3");
            Assert.Throws<ArgumentNullException>(() => _cameraBuilder.SetUsername(string.Empty));
        }

        [Fact]
        public void CameraBuilderShouldThrowErrorWhenEmptyPasswordIsSet()
        {
            _cameraBuilder.SetUrl("http://192.168.1.3");
            Assert.Throws<ArgumentNullException>(() => _cameraBuilder.SetPassword(string.Empty));
        }

        #endregion
    }
}
