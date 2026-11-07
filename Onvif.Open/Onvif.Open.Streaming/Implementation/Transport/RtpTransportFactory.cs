// Sarthak Killedar
// 2026-11-07

using System;
using System.Net;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Streaming.Implementation.Transport
{
    /// <summary>
    /// Factory selecting the RTP transport strategy.
    /// </summary>
    internal static class RtpTransportFactory
    {
        #region Public Methods

        public static IRtpTransport Create(StreamTransport transport, IPAddress serverAddress)
        {
            switch (transport)
            {
                case StreamTransport.Rtsp:
                    return new TcpInterleavedRtpTransport();
                case StreamTransport.Udp:
                    return new UdpRtpTransport(serverAddress);
                default:
                    throw new NotSupportedException($"Stream transport '{transport}' is not supported. Use StreamTransport.Rtsp or StreamTransport.Udp.");
            }
        }

        #endregion
    }
}
