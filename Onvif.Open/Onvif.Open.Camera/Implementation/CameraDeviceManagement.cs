// Sarthak Killedar
// 2016-03-06 @ 9:16 AM

using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;

namespace Onvif.Open.Camera.Implementation
{
    public class CameraDeviceManagement : ICameraDeviceManagement
    {
        #region Constructor

        private CameraDeviceManagement(IDeviceManagement deviceManagement)
        {
            DeviceManagement = deviceManagement;
        }

        #endregion

        #region Private Variables

        private IDeviceManagement DeviceManagement { get; }

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        internal static ICameraDeviceManagement CreateInstance(IDeviceManagement deviceManagement)
        {
            return new CameraDeviceManagement(deviceManagement);    
        }

        #endregion

        #region Public Methods

        public IDeviceManagement GetDeviceManagement()
        {
           return DeviceManagement;
        }

        #endregion

    }
}