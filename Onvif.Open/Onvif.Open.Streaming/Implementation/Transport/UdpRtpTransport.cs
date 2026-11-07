// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Streaming.Implementation.Rtsp;

namespace Onvif.Open.Streaming.Implementation.Transport
{
    /// <summary>
    /// RTP over UDP unicast: binds an even/odd RTP/RTCP port pair, accepts datagrams from the
    /// device only and restores packet order before depacketizing.
    /// </summary>
    internal sealed class UdpRtpTransport : IRtpTransport
    {
        #region Constructor

        public UdpRtpTransport(IPAddress serverAddress, int reorderCapacity = DefaultReorderCapacity)
        {
            _serverAddress = Normalize(serverAddress ?? throw new ArgumentNullException(nameof(serverAddress)));
            _reorderBuffer = new RtpReorderBuffer(reorderCapacity);
            BindPortPair(serverAddress.AddressFamily);
        }

        #endregion

        #region Private Variables

        private const int DefaultReorderCapacity = 64;
        private const int ReceiveBufferSize = 4 * 1024 * 1024;
        private const int MaxBindAttempts = 32;

        private readonly RtpReorderBuffer _reorderBuffer;
        private IPAddress _serverAddress;
        private UdpClient _rtp;
        private UdpClient _rtcp;
        private int _serverRtpPort = -1;
        private int _serverRtcpPort = -1;

        #endregion

        #region Public Variables

        public int ClientRtpPort { get; private set; }
        public int ClientRtcpPort => ClientRtpPort + 1;

        #endregion

        #region Public Methods

        public string GetRequestTransport()
        {
            return $"RTP/AVP;unicast;client_port={ClientRtpPort}-{ClientRtcpPort}";
        }

        public void Configure(string responseTransport)
        {
            if (TransportHeader.GetParameter(responseTransport, "interleaved") != null)
            {
                throw new NotSupportedException("The device refused RTP over UDP and answered with TCP interleaved transport. Use StreamTransport.Rtsp instead.");
            }

            if (TransportHeader.TryGetRange(responseTransport, "server_port", out var rtp, out var rtcp))
            {
                _serverRtpPort = rtp;
                _serverRtcpPort = rtcp;
            }

            // The device may stream from another address than the RTSP endpoint (e.g. a streaming proxy).
            if (IPAddress.TryParse(TransportHeader.GetParameter(responseTransport, "source") ?? string.Empty, out var source))
            {
                _serverAddress = Normalize(source);
            }
        }

        public async Task ReceiveAsync(RtspConnection connection, Action<byte[]> onRtpPacket, CancellationToken cancellationToken)
        {
            await OpenNatBindingsAsync(cancellationToken).ConfigureAwait(false);

            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var loops = new[]
                {
                    DrainRtspAsync(connection, stop.Token),
                    ReceiveRtpAsync(onRtpPacket, stop.Token),
                    DrainRtcpAsync(stop.Token)
                };

                var first = await Task.WhenAny(loops).ConfigureAwait(false);
                stop.Cancel();

                try
                {
                    await Task.WhenAll(loops).ConfigureAwait(false);
                }
                catch when (!first.IsFaulted)
                {
                    // Cancellation of the remaining loops.
                }

                await first.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _rtp?.Dispose();
            _rtcp?.Dispose();
        }

        #endregion

        #region Private Methods

        private void BindPortPair(AddressFamily family)
        {
            var any = family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;

            for (var attempt = 0; attempt < MaxBindAttempts; attempt++)
            {
                var rtp = new UdpClient(new IPEndPoint(any, 0));
                var port = ((IPEndPoint)rtp.Client.LocalEndPoint).Port;

                // RFC 3550: RTP on an even port, RTCP on the next (odd) one.
                if (port % 2 != 0 || port >= IPEndPoint.MaxPort)
                {
                    rtp.Dispose();
                    continue;
                }

                try
                {
                    _rtcp = new UdpClient(new IPEndPoint(any, port + 1));
                }
                catch (SocketException)
                {
                    rtp.Dispose();
                    continue;
                }

                _rtp = rtp;
                _rtp.Client.ReceiveBufferSize = ReceiveBufferSize;
                ClientRtpPort = port;
                return;
            }

            throw new IOException("Could not bind a free UDP port pair for RTP/RTCP.");
        }

        private async Task OpenNatBindingsAsync(CancellationToken cancellationToken)
        {
            // Small packets towards the device open NAT/firewall pinholes for the incoming stream.
            try
            {
                if (_serverRtpPort > 0)
                {
                    var rtp = new byte[] { 0x80, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                    await _rtp.SendAsync(rtp, new IPEndPoint(_serverAddress, _serverRtpPort), cancellationToken).ConfigureAwait(false);
                }

                if (_serverRtcpPort > 0)
                {
                    // Empty RTCP receiver report.
                    var rtcp = new byte[] { 0x80, 0xC9, 0x00, 0x01, 0, 0, 0, 0 };
                    await _rtcp.SendAsync(rtcp, new IPEndPoint(_serverAddress, _serverRtcpPort), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (SocketException)
            {
                // Not essential on a local network.
            }
        }

        private static async Task DrainRtspAsync(RtspConnection connection, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (await connection.ReadAsync(cancellationToken).ConfigureAwait(false) == null)
                {
                    throw new IOException("RTSP server closed the connection.");
                }
            }
        }

        private async Task ReceiveRtpAsync(Action<byte[]> onRtpPacket, CancellationToken cancellationToken)
        {
            var ready = new List<byte[]>();

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _rtp.ReceiveAsync(cancellationToken).ConfigureAwait(false);

                if (!Normalize(result.RemoteEndPoint.Address).Equals(_serverAddress))
                {
                    // Ignore datagrams that do not come from the device.
                    continue;
                }

                _reorderBuffer.Add(result.Buffer, ready);

                foreach (var packet in ready)
                {
                    onRtpPacket(packet);
                }

                ready.Clear();
            }
        }

        private async Task DrainRtcpAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _rtcp.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private static IPAddress Normalize(IPAddress address)
        {
            return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        }

        #endregion
    }
}
