using System;
using System.ServiceModel;
using System.ServiceModel.Description;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Abstract.Interface.Onvif;
using Onvif.Open.Device.Management.Implementation.Client;
using Onvif.Open.Device.Management.Ver10.DeviceManagement;

namespace Onvif.Open.Device.Management.Implementation.Builder
{
    public class DeviceDateTimeBuilder : IOnvifRequestBuilder<DeviceInformation, IDeviceDateTimeInformation>
    {
        #region Constructor

        private DeviceDateTimeBuilder()
        {

        }

        #endregion

        #region Private Variables

        private EndpointAddress _endpointAddress;
        private DeviceClient _deviceClient;
        private IEndpointBehavior _authenticator;
        private ICameraInformation _camera;

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods


        #endregion

        #region Public Methods

        public IDeviceDateTimeInformation Build()
        {
            return DeviceDateTime.CreateInstance(_deviceClient);
        }

        public IOnvifRequestBuilder<DeviceInformation, IDeviceDateTimeInformation> SetProperties(DeviceInformation obj)
        {
            throw new ActionNotSupportedException("This Builder does not support properties.");
        }

        public IOnvifRequestBuilder<DeviceInformation, IDeviceDateTimeInformation> SetCamera(ICameraInformation camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));

            return this;
        }

        public IOnvifRequestBuilder<DeviceInformation, IDeviceDateTimeInformation> SetEndpoint()
        {
            if (_camera == null)
            {
                throw new InvalidOperationException("Camera must be set before calling this method.");
            }

            _endpointAddress = new EndpointAddress(_camera.CameraUri);

            return this;
        }

        public IOnvifRequestBuilder<DeviceInformation, IDeviceDateTimeInformation> SetClient()
        {
            _deviceClient =
                DeviceClientBuilder.GetBuilder()
                    .SetHttpTransportBinding()
                    .SetMessageElement()
                    .SetEndpoint(_endpointAddress)
                    .SetCustomeBinding()
                    .SetClient()
                    .SetAuthenticator(_authenticator)
                    .Build();

            return this;
        }

        public IOnvifRequestBuilder<DeviceInformation, IDeviceDateTimeInformation> SetEndpoint(IOnvifEndpoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            _endpointAddress = new EndpointAddress(endpoint.EndPointAddress);

            return this;
        }

        public IOnvifRequestBuilder<DeviceInformation, IDeviceDateTimeInformation> SetAuthentication(IEndpointBehavior authenticator)
        {
            _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));

            return this;
        }

        public static DeviceDateTimeBuilder GetBuilder()
        {
            return new DeviceDateTimeBuilder();
        }
        #endregion

    }
}