// Sarthak Killedar
// 2016-03-06 @ 10:01 PM

using System;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using Onvif.Open.Core.Abstract.Interface.Onvif;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Device.Management.Ver10.DeviceManagement;

namespace Onvif.Open.Device.Management.Implementation.Builder
{
    public class DeviceClientBuilder : IOnvifClienBuilder<DeviceClient>
    {
        #region Constructor

        private DeviceClientBuilder()
        {
            
        }

        #endregion

        #region Private Variables

        private EndpointAddress _endpointAddress;
        private HttpTransportBindingElement _httpTransportBinding;
        private TextMessageEncodingBindingElement _messageElement;
        private CustomBinding _customBinding;
        private DeviceClient _deviceClient;

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        public DeviceClient Build()
        {
            return _deviceClient;
        }

        public IOnvifClienBuilder<DeviceClient> SetHttpTransportBinding()
        {
            _httpTransportBinding = new HttpTransportBindingElement()
            {
                AuthenticationScheme = AuthenticationSchemes.Digest
            };
            return this;
        }

        public IOnvifClienBuilder<DeviceClient> SetMessageElement()
        {
            _messageElement = new TextMessageEncodingBindingElement()
            {
                MessageVersion = MessageVersion.CreateVersion(EnvelopeVersion.Soap12, AddressingVersion.None)
            };
            return this;
        }

        public IOnvifClienBuilder<DeviceClient> SetCustomeBinding()
        {
            if (_messageElement == null || _httpTransportBinding == null)
            {
                throw new InvalidOperationException("This operation can not be called directly.");
            }

            _customBinding = new CustomBinding(_messageElement, _httpTransportBinding);
            return this;
        }

        public IOnvifClienBuilder<DeviceClient> SetClient()
        {
            if (_customBinding == null || _endpointAddress == null)
            {
                throw new InvalidOperationException("This operation can not be called directly.");
            }

            _deviceClient = new DeviceClient(_customBinding, _endpointAddress);
            return this;
        }

        public IOnvifClienBuilder<DeviceClient> SetEndpoint(EndpointAddress endpointAddress)
        {
            _endpointAddress = endpointAddress;
            return this;
        }

        public IOnvifClienBuilder<DeviceClient> SetAuthenticator(IEndpointBehavior behavior)
        {
            _deviceClient.Endpoint.Behaviors.Add(behavior);
            return this;
        }

        internal static DeviceClientBuilder GetBuilder()
        {
            return new DeviceClientBuilder();
        }
        #endregion

    }
}