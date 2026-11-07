// Sarthak Killedar
// 2026-11-07

using System;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Streaming.Implementation.Rtsp;

namespace Onvif.Open.Streaming.Implementation.Transport
{
    /// <summary>
    /// Strategy deciding how RTP packets travel from the device: interleaved in the RTSP TCP
    /// connection or as UDP datagrams.
    /// </summary>
    internal interface IRtpTransport : IDisposable
    {
        #region Public Methods

        /// <summary>Value of the "Transport" header sent with SETUP.</summary>
        string GetRequestTransport();

        /// <summary>Applies the "Transport" header returned by the device for SETUP.</summary>
        void Configure(string responseTransport);

        /// <summary>
        /// Receives RTP packets until cancelled or the connection fails. Also drains the RTSP
        /// connection (keep-alive responses), which every transport has to do.
        /// </summary>
        Task ReceiveAsync(RtspConnection connection, Action<byte[]> onRtpPacket, CancellationToken cancellationToken);

        #endregion
    }
}
