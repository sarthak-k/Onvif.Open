// Sarthak Killedar
// 2016-03-06 @ 1:19 AM

using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Device.Management.Implementation;
using Onvif.Open.Device.Management.Implementation.Builder;

namespace Onvif.Open.Device.Management.Interface
{
    internal interface IDeviceManagementBuilder
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

        IDeviceManagement Build();
        DeviceManagementBuilder SetCamera(ICameraInformation camera);

        #endregion

    }
}