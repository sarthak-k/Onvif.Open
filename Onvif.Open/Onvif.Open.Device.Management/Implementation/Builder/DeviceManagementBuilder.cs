// Sarthak Killedar
// 2016-03-06 @ 1:20 AM

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Device.Management.Implementation.Client;
using Onvif.Open.Device.Management.Interface;

namespace Onvif.Open.Device.Management.Implementation.Builder
{
    public class DeviceManagementBuilder : IDeviceManagementBuilder
    {
        #region Constructor

        private DeviceManagementBuilder()
        {
            
        }

        #endregion

        #region Private Variables

        private ICameraInformation Camera { get; set; }

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        private bool IsCameraNull(ICameraInformation camera)
        {
            return camera == null;
        }
        #endregion

        #region Public Methods

        public IDeviceManagement Build()
        {
            return DeviceManagement.Create(Camera);
        }

        public DeviceManagementBuilder SetCamera(ICameraInformation camera)
        {
            if (IsCameraNull(camera))
            {
                throw new ArgumentNullException(nameof(camera));
            }

            Camera = camera;

            return this;
        }

        public static DeviceManagementBuilder GetBuilder()
        {
            return new DeviceManagementBuilder();
        }

        #endregion

    }
}