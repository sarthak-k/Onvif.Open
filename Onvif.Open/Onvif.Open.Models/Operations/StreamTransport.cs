// Sarthak Killedar
namespace Onvif.Open.Core.Operations
{
    /// <summary>
    /// Transport requested from the ONVIF media service when asking for a stream URI.
    /// </summary>
    public enum StreamTransport
    {
        /// <summary>RTP interleaved over the RTSP TCP connection (most firewall/NAT friendly).</summary>
        Rtsp = 1,

        /// <summary>RTP over UDP, negotiated through RTSP.</summary>
        Udp = 2,

        /// <summary>RTP over RTSP tunnelled through HTTP.</summary>
        Http = 3
    }
}
