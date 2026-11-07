// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Discovery;
using Onvif.Open.Core.Discovery;
using Onvif.Open.Discovery.Implementation.Protocol;

namespace Onvif.Open.Discovery.Implementation
{
    /// <summary>
    /// WS-Discovery client: sends Probe messages to the multicast group on every IPv4 interface (and to
    /// the configured unicast targets) and collects the ProbeMatches answers.
    /// Build it with <see cref="Builder.CameraDiscoveryBuilder"/>.
    /// </summary>
    public sealed class CameraDiscovery : ICameraDiscovery
    {
        #region Constructor

        private CameraDiscovery(TimeSpan timeout, bool useMulticast, IReadOnlyList<IPEndPoint> targets)
        {
            _timeout = timeout;
            _useMulticast = useMulticast;
            _targets = targets;
        }

        #endregion

        #region Private Variables

        private static readonly IPEndPoint MulticastEndpoint = new IPEndPoint(IPAddress.Parse(ProbeMessage.MulticastAddress), ProbeMessage.Port);

        /// <summary>UDP is lossy: every probe is sent this many times (WS-Discovery recommends retransmission).</summary>
        private const int Repeats = 2;
        private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(150);

        private readonly TimeSpan _timeout;
        private readonly bool _useMulticast;
        private readonly IReadOnlyList<IPEndPoint> _targets;

        #endregion

        #region Public Methods

        public async Task<IReadOnlyList<DiscoveredDevice>> FindAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            await foreach (var device in DiscoverAsync(cancellationToken).ConfigureAwait(false))
            {
                devices.Add(device);
            }

            return devices;
        }

        public async IAsyncEnumerable<DiscoveredDevice> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var sockets = CreateSockets();

            try
            {
                using (var window = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    window.CancelAfter(_timeout);

                    var messageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var probes = ProbeMessage.DeviceTypes.Select(type =>
                    {
                        var id = ProbeMessage.NewMessageId();
                        messageIds.Add(id);
                        return ProbeMessage.Create(id, type);
                    }).ToList();

                    var found = Channel.CreateUnbounded<DiscoveredDevice>();
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var receivers = sockets.Select(s => ReceiveAsync(s.Client, messageIds, seen, found.Writer, window.Token)).ToList();
                    var sender = SendAsync(sockets, probes, window.Token);

                    _ = Task.WhenAll(receivers.Append(sender)).ContinueWith(_ => found.Writer.TryComplete(), TaskScheduler.Default);

                    var reader = found.Reader;
                    while (true)
                    {
                        bool available;
                        try
                        {
                            available = await reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            available = false;
                        }

                        if (!available)
                        {
                            break;
                        }

                        while (reader.TryRead(out var device))
                        {
                            yield return device;
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                foreach (var socket in sockets)
                {
                    socket.Client.Dispose();
                }
            }
        }

        internal static CameraDiscovery CreateInstance(TimeSpan timeout, bool useMulticast, IReadOnlyList<IPEndPoint> targets)
        {
            return new CameraDiscovery(timeout, useMulticast, targets);
        }

        #endregion

        #region Private Methods

        private List<(UdpClient Client, bool Multicast)> CreateSockets()
        {
            var sockets = new List<(UdpClient, bool)>();

            if (_useMulticast)
            {
                foreach (var address in MulticastCapableAddresses())
                {
                    try
                    {
                        var client = new UdpClient(new IPEndPoint(address, 0));
                        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                        client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                        sockets.Add((client, true));
                    }
                    catch (SocketException)
                    {
                        // Interface went away or does not allow binding: skip it.
                    }
                }
            }

            if (_targets.Count > 0 || sockets.Count == 0)
            {
                // Unicast probes, and multicast through the default route when no interface could be bound.
                sockets.Add((new UdpClient(new IPEndPoint(IPAddress.Any, 0)), sockets.Count == 0 && _useMulticast));
            }

            return sockets;
        }

        private static IEnumerable<IPAddress> MulticastCapableAddresses()
        {
            NetworkInterface[] interfaces;
            try
            {
                interfaces = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                yield break;
            }

            foreach (var nic in interfaces)
            {
                if (nic.OperationalStatus != OperationalStatus.Up || !nic.SupportsMulticast || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        yield return unicast.Address;
                    }
                }
            }
        }

        private async Task SendAsync(IReadOnlyList<(UdpClient Client, bool Multicast)> sockets, IReadOnlyList<byte[]> probes, CancellationToken cancellationToken)
        {
            try
            {
                for (var repeat = 0; repeat < Repeats; repeat++)
                {
                    foreach (var (client, multicast) in sockets)
                    {
                        foreach (var probe in probes)
                        {
                            if (multicast)
                            {
                                await TrySendAsync(client, probe, MulticastEndpoint, cancellationToken).ConfigureAwait(false);
                            }

                            foreach (var target in _targets.Where(_ => client.Client.LocalEndPoint is IPEndPoint local && local.Address.Equals(IPAddress.Any)))
                            {
                                await TrySendAsync(client, probe, target, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }

                    await Task.Delay(RepeatInterval, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task TrySendAsync(UdpClient client, byte[] probe, IPEndPoint target, CancellationToken cancellationToken)
        {
            try
            {
                await client.SendAsync(probe, target, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // Unreachable network on this interface: the other interfaces may still work.
            }
        }

        private static async Task ReceiveAsync(UdpClient client, ISet<string> messageIds, ISet<string> seen, ChannelWriter<DiscoveredDevice> found, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    // e.g. ICMP port unreachable from a unicast target: keep listening.
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                var devices = ProbeMatchParser.Parse(result.Buffer, result.RemoteEndPoint.Address, out var relatesTo);

                // Ignore answers to somebody else's probe (devices answer every probe on the multicast group to the sender only,
                // but tolerate devices that omit RelatesTo).
                if (relatesTo != null && !messageIds.Contains(relatesTo))
                {
                    continue;
                }

                foreach (var device in devices)
                {
                    bool added;
                    lock (seen)
                    {
                        added = seen.Add(device.EndpointReference);
                    }

                    if (added)
                    {
                        found.TryWrite(device);
                    }
                }
            }
        }

        #endregion
    }
}
