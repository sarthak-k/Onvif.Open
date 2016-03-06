// Sarthak Killedar
// 2016-03-06 @ 9:15 AM

using System;
using Onvif.Open.Camera.Interface;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Device.Management.Implementation;
using Onvif.Open.Device.Management.Implementation.Builder;

namespace Onvif.Open.Camera.Implementation
{
    public class CameraDeviceManagementBuilder : ICameraDeviceManagementBuilder
    {
        #region Constructor

        private CameraDeviceManagementBuilder()
        {
            
        }

        #endregion

        #region Private Variables

        private IDeviceManagement DeviceManagement { get; set; }

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        public ICameraDeviceManagement Build()
        {
            return CameraDeviceManagement.CreateInstance(DeviceManagement);
        }

        public CameraDeviceManagementBuilder SetDeviceManagement(ICameraInformation camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            DeviceManagement = DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build();
            return this;
        }

        internal static CameraDeviceManagementBuilder GetBuilder()
        {
            return new CameraDeviceManagementBuilder();
        }

        #endregion

    }
}