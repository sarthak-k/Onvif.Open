// Sarthak Killedar
// 2016-03-06 @ 10:25 AM

using System;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Onvif;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Device.Management.Implementation.Client;
using Onvif.Open.Device.Management.Ver10.DeviceManagement;

namespace Onvif.Open.Device.Management.Implementation.Builder
{
    internal class DeviceInfoBuilder : IOnvifRequestBuilder<DeviceInfo>
    {
        #region Constructor

        private DeviceInfoBuilder()
        {
            
        }

        #endregion

        #region Private Variables

        private EndpointAddress _endpointAddress;
        private IEndpointBehavior _authenticator;
        private DeviceClient _deviceClient;
        private ICameraInformation _camera;

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

 
        #endregion

        #region Public Methods

        public DeviceInfo Build()
        {
            return DeviceInfo.CreateInstance(_deviceClient);
        }

        public IOnvifRequestBuilder<DeviceInfo> SetProperties(DeviceInfo obj)
        {
            throw new ActionNotSupportedException("This Builder does not support properties.");
        }

        public IOnvifRequestBuilder<DeviceInfo> SetCamera(ICameraInformation camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            _camera = camera;

            return this;
        }

        public IOnvifRequestBuilder<DeviceInfo> SetEndpoint()
        {
            if (_camera == null)
            {
                throw new InvalidOperationException("Camera must be set before calling this method.");
            }

            _endpointAddress = new EndpointAddress(_camera.CameraUri);

            return this;
        }
        
        public IOnvifRequestBuilder<DeviceInfo> SetClient()
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

        public IOnvifRequestBuilder<DeviceInfo> SetEndpoint(IOnvifEndpoint endpoint)
        {
            if (endpoint == null)
            {
                throw new ArgumentNullException(nameof(endpoint));
            }

            _endpointAddress = new EndpointAddress(endpoint.EndPointAddress);

            return this;
        }

        public IOnvifRequestBuilder<DeviceInfo> SetAuthentication(IEndpointBehavior authenticator)
        {
            if (authenticator == null)
            {
                throw new ArgumentNullException(nameof(authenticator));
            }

            _authenticator = authenticator;

            return this;
        }

        public static DeviceInfoBuilder GetBuilder()
        {
            return new DeviceInfoBuilder();
        }
        #endregion

    }
}