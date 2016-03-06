using System;
using Onvif.Open.Core.Abstract.Interface.Device;

namespace Onvif.Open.Core.Abstract.Interface.Camera
{
    public interface ICamera
    {
        #region Constructor

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        ICameraDeviceManagement Initialize();
        ICameraInformation GetCameraInformation();

        #endregion
    }
}