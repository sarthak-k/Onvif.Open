// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Streaming.Implementation.Rtsp;

namespace Onvif.Open.Streaming.Implementation.Transport
{
    /// <summary>
    /// RTP interleaved in the RTSP TCP connection (RFC 2326 §10.12). Works through firewalls and NAT.
    /// </summary>
    internal sealed class TcpInterleavedRtpTransport : IRtpTransport
    {
        #region Private Variables

        private int _rtpChannel;

        #endregion

        #region Public Variables

        public int RtpChannel => _rtpChannel;

        #endregion

        #region Public Methods

        public string GetRequestTransport()
        {
            return "RTP/AVP/TCP;unicast;interleaved=0-1";
        }

        public void Configure(string responseTransport)
        {
            _rtpChannel = TransportHeader.TryGetRange(responseTransport, "interleaved", out var rtp, out _) ? rtp : 0;
        }

        public async Task ReceiveAsync(RtspConnection connection, Func<byte[], ValueTask> onRtpPacket, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var incoming = await connection.ReadAsync(cancellationToken).ConfigureAwait(false)
                               ?? throw new IOException("RTSP server closed the connection.");

                // RTCP, keep-alive responses and server requests need no handling.
                if (incoming.IsPacket && incoming.Channel == _rtpChannel)
                {
                    await onRtpPacket(incoming.Data).ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
        }

        #endregion
    }
}
