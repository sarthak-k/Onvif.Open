// Sarthak Killedar
// 2016-03-06 @ 1:09 AM

using System;
using System.ServiceModel;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Device.Management.Ver10.DeviceManagement;

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

            return deviceInfo.Get();
        }

        public IDeviceDateAndTime GetDeviceDateTime()
        {
            var authenticator = PasswordDigestBuilder.GetBuilder().SetCamera(_camera).Build();

            var deviceInfo =
                DeviceDateTimeBuilder.GetBuilder()
                    .SetCamera(_camera)
                    .SetEndpoint()
                    .SetAuthentication(authenticator)
                    .SetClient()
                    .Build();

            return deviceInfo.Get();
        }
    
        public Uri GetMediaServiceUri()
        {
            var authenticator = PasswordDigestBuilder.GetBuilder().SetCamera(_camera).Build();

            var client = DeviceClientBuilder.GetBuilder()
                .SetHttpTransportBinding()
                .SetMessageElement()
                .SetEndpoint(new EndpointAddress(_camera.CameraUri))
                .SetCustomeBinding()
                .SetClient()
                .SetAuthenticator(authenticator)
                .Build();

            try
            {
                var capabilities = client.GetCapabilities(new[] { CapabilityCategory.Media });
                var xaddr = capabilities?.Media?.XAddr;

                if (!string.IsNullOrWhiteSpace(xaddr) && Uri.TryCreate(xaddr.Trim(), UriKind.Absolute, out var mediaUri))
                {
                    return mediaUri;
                }
            }
            catch (FaultException)
            {
                // Some devices do not implement GetCapabilities; most of them serve every service on the device endpoint.
            }
            finally
            {
                CloseClient(client);
            }

            return _camera.CameraUri;
        }

        private static void CloseClient(ICommunicationObject client)
        {
            try
            {
                if (client.State == CommunicationState.Faulted)
                {
                    client.Abort();
                }
                else
                {
                    client.Close();
                }
            }
            catch (CommunicationException)
            {
                client.Abort();
            }
            catch (TimeoutException)
            {
                client.Abort();
            }
        }
    }
}
