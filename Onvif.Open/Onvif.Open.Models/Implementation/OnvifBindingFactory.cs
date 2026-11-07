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

        public static Binding CreateBinding(Uri serviceUri = null)
        {
            var encoding = new TextMessageEncodingBindingElement
            {
                MessageVersion = MessageVersion.CreateVersion(EnvelopeVersion.Soap12, AddressingVersion.None)
            };

            var useTls = serviceUri != null && string.Equals(serviceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var transport = useTls ? new HttpsTransportBindingElement() : new HttpTransportBindingElement();

            // Devices may require HTTP Digest in addition to (or instead of) WS-Security UsernameToken.
            // Credentials are only sent when the device answers with a 401 challenge.
            transport.AuthenticationScheme = AuthenticationSchemes.Digest;
            transport.MaxReceivedMessageSize = MaxMessageSize;
            transport.MaxBufferSize = (int)MaxMessageSize;

            return new CustomBinding(encoding, transport);
        }
    }
}
