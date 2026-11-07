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
            return GetServiceUri(CapabilityCategory.Media, c => c?.Media?.XAddr);
        }

        public Uri GetEventServiceUri()
        {
            return GetServiceUri(CapabilityCategory.Events, c => c?.Events?.XAddr);
        }

        public Uri GetPtzServiceUri()
        {
            // Unlike media and events, PTZ is optional: a device reporting capabilities without PTZ has none.
            return GetServiceUri(CapabilityCategory.PTZ, c => c?.PTZ?.XAddr, noServiceWhenMissing: true);
        }

        private Uri GetServiceUri(CapabilityCategory category, Func<Capabilities, string> selectAddress, bool noServiceWhenMissing = false)
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
                var xaddr = selectAddress(client.GetCapabilities(new[] { category }));

                if (!string.IsNullOrWhiteSpace(xaddr) && Uri.TryCreate(xaddr.Trim(), UriKind.Absolute, out var serviceUri))
                {
                    return serviceUri;
                }

                if (noServiceWhenMissing)
                {
                    return null;
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

        public TimeSpan? GetClockOffset()
        {
            // No WS-Security header: a token stamped with a skewed clock could itself be rejected.
            var client = DeviceClientBuilder.GetBuilder()
                .SetHttpTransportBinding()
                .SetMessageElement()
                .SetEndpoint(new EndpointAddress(_camera.CameraUri))
                .SetCustomeBinding()
                .SetClient()
                .Build();

            try
            {
                var before = System.DateTime.UtcNow;
                var deviceTime = client.GetSystemDateAndTime();
                var after = System.DateTime.UtcNow;

                var utc = deviceTime?.UTCDateTime;
                if (utc?.Date == null || utc.Time == null)
                {
                    return null;
                }

                var device = new System.DateTime(utc.Date.Year, utc.Date.Month, utc.Date.Day, utc.Time.Hour, utc.Time.Minute, utc.Time.Second, DateTimeKind.Utc);

                // The device read its clock roughly halfway through the round trip.
                var local = before + TimeSpan.FromTicks((after - before).Ticks / 2);
                return device - local;
            }
            finally
            {
                CloseClient(client);
            }
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
