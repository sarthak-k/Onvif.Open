// Sarthak Killedar
// 2026-11-07

using System;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using Onvif.Open.Core.Abstract.Interface.Onvif;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Media.Ver10;

namespace Onvif.Open.Media.Implementation.Builder
{
    internal class MediaClientBuilder : IOnvifClienBuilder<MediaClient>
    {
        #region Constructor

        private MediaClientBuilder()
        {

        }

        #endregion

        #region Private Variables

        private EndpointAddress _endpointAddress;
        private Binding _binding;
        private MediaClient _mediaClient;

        #endregion

        #region Public Methods

        public MediaClient Build()
        {
            return _mediaClient ?? throw new InvalidOperationException("SetClient must be called before Build.");
        }

        public IOnvifClienBuilder<MediaClient> SetHttpTransportBinding()
        {
            // The transport is part of the shared ONVIF binding created in SetCustomeBinding.
            return this;
        }

        public IOnvifClienBuilder<MediaClient> SetMessageElement()
        {
            // The SOAP 1.2 message element is part of the shared ONVIF binding created in SetCustomeBinding.
            return this;
        }

        public IOnvifClienBuilder<MediaClient> SetCustomeBinding()
        {
            if (_endpointAddress == null)
            {
                throw new InvalidOperationException("SetEndpoint must be called before SetCustomeBinding.");
            }

            _binding = OnvifBindingFactory.CreateBinding(_endpointAddress.Uri);
            return this;
        }

        public IOnvifClienBuilder<MediaClient> SetClient()
        {
            if (_binding == null || _endpointAddress == null)
            {
                throw new InvalidOperationException("This operation can not be called directly.");
            }

            _mediaClient = new MediaClient(_binding, _endpointAddress);
            return this;
        }

        public IOnvifClienBuilder<MediaClient> SetEndpoint(EndpointAddress endpointAddress)
        {
            _endpointAddress = endpointAddress ?? throw new ArgumentNullException(nameof(endpointAddress));
            return this;
        }

        public IOnvifClienBuilder<MediaClient> SetAuthenticator(IEndpointBehavior behavior)
        {
            if (_mediaClient == null)
            {
                throw new InvalidOperationException("SetClient must be called before SetAuthenticator.");
            }

            if (behavior == null)
            {
                throw new ArgumentNullException(nameof(behavior));
            }

            _mediaClient.Endpoint.EndpointBehaviors.Add(behavior);
            (behavior as PasswordDigestBehaviour)?.ConfigureCredentials(_mediaClient.ClientCredentials);
            return this;
        }

        internal static MediaClientBuilder GetBuilder()
        {
            return new MediaClientBuilder();
        }

        #endregion
    }
}
