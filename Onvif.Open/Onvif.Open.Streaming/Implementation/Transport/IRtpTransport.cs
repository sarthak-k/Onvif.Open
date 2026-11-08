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

        /// <summary>Value of the "Transport" header sent with the SETUP of track <paramref name="track"/> (0, 1, ...).</summary>
        string GetRequestTransport(int track);

        /// <summary>Applies the "Transport" header returned by the device for the SETUP of track <paramref name="track"/>.</summary>
        void Configure(int track, string responseTransport);

        /// <summary>
        /// Receives RTP packets until cancelled or the connection fails. Also drains the RTSP
        /// connection (keep-alive responses), which every transport has to do.
        /// </summary>
        /// <remarks><paramref name="onRtpPacket"/> receives the track index and the packet.</remarks>
        Task ReceiveAsync(RtspConnection connection, Func<int, byte[], ValueTask> onRtpPacket, CancellationToken cancellationToken);

        #endregion
    }
}
