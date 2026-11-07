// Sarthak Killedar
// 2016-03-06 @ 1:09 AM

using System;
using System.Linq;
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

        public Uri GetImagingServiceUri()
        {
            return GetServiceUri(CapabilityCategory.Imaging, c => c?.Imaging?.XAddr, noServiceWhenMissing: true);
        }

        public System.Collections.Generic.IReadOnlyList<Core.DeviceIo.RelayOutput> GetRelayOutputs()
        {
            return WithClient(client => (client.GetRelayOutputs() ?? Array.Empty<RelayOutput>())
                .Where(r => !string.IsNullOrEmpty(r?.token))
                .Select(r => new Core.DeviceIo.RelayOutput(
                    r.token,
                    r.Properties?.Mode == RelayMode.Bistable ? Core.DeviceIo.RelayMode.Bistable : Core.DeviceIo.RelayMode.Monostable,
                    TryParseDuration(r.Properties?.DelayTime),
                    r.Properties?.IdleState == RelayIdleState.open ? Core.DeviceIo.IdleState.Open : Core.DeviceIo.IdleState.Closed))
                .ToList());
        }

        public void SetRelayOutputState(string relayToken, bool active)
        {
            RequireToken(relayToken);
            WithClient(client =>
            {
                client.SetRelayOutputState(relayToken, active ? RelayLogicalState.active : RelayLogicalState.inactive);
                return true;
            });
        }

        public void SetRelayOutputSettings(string relayToken, Core.DeviceIo.RelayMode mode, TimeSpan delayTime, Core.DeviceIo.IdleState idleState)
        {
            RequireToken(relayToken);
            WithClient(client =>
            {
                client.SetRelayOutputSettings(relayToken, new RelayOutputSettings
                {
                    Mode = mode == Core.DeviceIo.RelayMode.Bistable ? RelayMode.Bistable : RelayMode.Monostable,
                    DelayTime = System.Xml.XmlConvert.ToString(delayTime),
                    IdleState = idleState == Core.DeviceIo.IdleState.Open ? RelayIdleState.open : RelayIdleState.closed
                });
                return true;
            });
        }

        private T WithClient<T>(Func<DeviceClient, T> call)
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
                return call(client);
            }
            finally
            {
                CloseClient(client);
            }
        }

        private static void RequireToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentNullException(nameof(token));
            }
        }

        private static TimeSpan? TryParseDuration(string duration)
        {
            try
            {
                return string.IsNullOrWhiteSpace(duration) ? (TimeSpan?)null : System.Xml.XmlConvert.ToTimeSpan(duration.Trim());
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public Uri FindServiceUri(string serviceNamespace)
        {
            if (string.IsNullOrWhiteSpace(serviceNamespace))
            {
                throw new ArgumentNullException(nameof(serviceNamespace));
            }

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
                var services = client.GetServices(false) ?? Array.Empty<Service>();
                var xaddr = services.FirstOrDefault(s => string.Equals(s?.Namespace?.Trim(), serviceNamespace, StringComparison.OrdinalIgnoreCase))?.XAddr;

                return !string.IsNullOrWhiteSpace(xaddr) && Uri.TryCreate(xaddr.Trim(), UriKind.Absolute, out var uri) ? uri : null;
            }
            catch (Exception ex) when ((ex is FaultException || ex is ProtocolException) && !OnvifFaults.IsAuthenticationFailure(ex))
            {
                // GetServices is mandatory since ONVIF 2.0, but older devices may not implement it.
                // Authentication failures are not "service missing": they propagate.
                return null;
            }
            finally
            {
                CloseClient(client);
            }
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
