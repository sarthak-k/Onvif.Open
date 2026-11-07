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

        // Interleaved RTP channel -> track index.
        private readonly System.Collections.Generic.Dictionary<int, int> _tracks = new System.Collections.Generic.Dictionary<int, int>();

        #endregion

        #region Public Variables

        /// <summary>RTP channel of the first track.</summary>
        public int RtpChannel
        {
            get
            {
                foreach (var entry in _tracks)
                {
                    if (entry.Value == 0)
                    {
                        return entry.Key;
                    }
                }

                return 0;
            }
        }

        #endregion

        #region Public Methods

        public string GetRequestTransport(int track)
        {
            return $"RTP/AVP/TCP;unicast;interleaved={track * 2}-{(track * 2) + 1}";
        }

        public void Configure(int track, string responseTransport)
        {
            // The device may assign other channels than requested.
            _tracks[TransportHeader.TryGetRange(responseTransport, "interleaved", out var rtp, out _) ? rtp : track * 2] = track;
        }

        public async Task ReceiveAsync(RtspConnection connection, Func<int, byte[], ValueTask> onRtpPacket, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var incoming = await connection.ReadAsync(cancellationToken).ConfigureAwait(false)
                               ?? throw new IOException("RTSP server closed the connection.");

                // RTCP, keep-alive responses and server requests need no handling.
                if (incoming.IsPacket && _tracks.TryGetValue(incoming.Channel, out var track))
                {
                    await onRtpPacket(track, incoming.Data).ConfigureAwait(false);
                }
            }
        }

        public void Dispose()
        {
        }

        #endregion
    }
}
