// Sarthak Killedar
// 2016-03-06 @ 1:09 AM

using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Device.Management.Implementation.Builder;

namespace Onvif.Open.Device.Management.Implementation.Client
{
    public class DeviceManagement : IDeviceManagement
    {
        #region Constructor

        private DeviceManagement(ICameraInformation camera)
        {
            _camera = camera;
        }

        #endregion

        #region Private Variables

        private readonly ICameraInformation _camera;

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        internal static IDeviceManagement Create(ICameraInformation camera)
        {
            return new DeviceManagement(camera);
        }

        #endregion

        #region Public Methods

        #endregion

        public IDeviceInfo GetDeviceInfo()
        {
            var authenticator = PasswordDigestBuilder.GetBuilder().SetCamera(_camera).Build();

            var deviceInfo =
                DeviceInfoBuilder.GetBuilder()
                    .SetCamera(_camera)
                    .SetEndpoint()
                    .SetAuthentication(authenticator)
                    .SetClient()
                    .Build();

            return deviceInfo.Initialize();
        }
    }
}