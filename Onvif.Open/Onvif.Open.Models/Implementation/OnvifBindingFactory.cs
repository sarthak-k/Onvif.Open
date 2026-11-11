// Sarthak Killedar
using System;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace Onvif.Open.Core.Implementation
{
    /// <summary>
    /// Creates the SOAP 1.2 / HTTP binding used by every ONVIF service client.
    /// </summary>
    public static class OnvifBindingFactory
    {
        public const long MaxMessageSize = 16 * 1024 * 1024;

        /// <param name="serviceUri">Selects HTTPS when the address is https.</param>
        /// <param name="useAddressing">
        /// WS-Addressing 1.0 headers (To, Action, MessageID and reference parameters): required by the
        /// Events service, whose subscriptions are WS-Addressing endpoint references.
        /// </param>
        public static Binding CreateBinding(Uri serviceUri = null, bool useAddressing = false)
        {
            var encoding = new TextMessageEncodingBindingElement
            {
                MessageVersion = MessageVersion.CreateVersion(EnvelopeVersion.Soap12, useAddressing ? AddressingVersion.WSAddressing10 : AddressingVersion.None)
            };

            var useTls = serviceUri != null && string.Equals(serviceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var transport = useTls ? new HttpsTransportBindingElement() : new HttpTransportBindingElement();

            // Devices may require HTTP Digest in addition to (or instead of) WS-Security UsernameToken.
            // Credentials are only sent when the device answers with a 401 challenge.
            transport.AuthenticationScheme = AuthenticationSchemes.Digest;
            transport.MaxReceivedMessageSize = MaxMessageSize;
            transport.MaxBufferSize = (int)MaxMessageSize;

            // Every SOAP request asks the camera network rule first (a gateway in the cloud reaches only public addresses).
            return new CustomBinding(encoding, CreateCameraNetworkElement(), transport);
        }

        /// <summary>
        /// Goes before the HTTP transport of every ONVIF binding: each request asks the camera network rule
        /// (<see cref="Networking.CameraNetwork"/>) before it is sent.
        /// </summary>
        public static BindingElement CreateCameraNetworkElement() => new CameraNetworkBindingElement();

        /// <summary>Puts <see cref="Networking.CameraNetwork.CheckingHandler"/> in front of WCF's HTTP handler.</summary>
        private sealed class CameraNetworkBindingElement : BindingElement
        {
            private static readonly Func<System.Net.Http.HttpClientHandler, System.Net.Http.HttpMessageHandler> Wrap = handler =>
            {
                if (Networking.CameraNetwork.IsRestricted)
                {
                    // A redirect would be followed below the check.
                    handler.AllowAutoRedirect = false;
                }

                return new Networking.CameraNetwork.CheckingHandler(handler);
            };

            public override BindingElement Clone() => new CameraNetworkBindingElement();

            public override T GetProperty<T>(BindingContext context) => context.GetInnerProperty<T>();

            public override bool CanBuildChannelFactory<TChannel>(BindingContext context) => context.CanBuildInnerChannelFactory<TChannel>();

            public override IChannelFactory<TChannel> BuildChannelFactory<TChannel>(BindingContext context)
            {
                context.BindingParameters.Remove<Func<System.Net.Http.HttpClientHandler, System.Net.Http.HttpMessageHandler>>();
                context.BindingParameters.Add(Wrap);
                return context.BuildInnerChannelFactory<TChannel>();
            }
        }
    }
}
