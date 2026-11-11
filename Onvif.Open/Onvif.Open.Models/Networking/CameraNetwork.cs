// Sarthak Killedar
// 2026-11-11

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Onvif.Open.Core.Networking
{
    /// <summary>A camera address this process may not connect to (see <see cref="CameraNetwork"/>).</summary>
    public sealed class CameraAddressRefusedException : IOException
    {
        public CameraAddressRefusedException(string host)
            : base($"{host} is not on the internet: a gateway in the cloud only connects to cameras with public addresses. Record this camera with a gateway at its site.")
        {
            Host = host;
        }

        public string Host { get; }
    }

    /// <summary>
    /// Which addresses this process connects to cameras at. Every connection to a camera (RTSP, ONVIF, snapshots) asks
    /// here, after the name is resolved and at the address actually used, so a camera (or its DNS) cannot point a
    /// gateway in the cloud at the cloud's own network: private, loopback, link-local (cloud metadata), CGNAT and other
    /// special-purpose ranges.
    /// </summary>
    public static class CameraNetwork
    {
        /// <summary>Any address (a gateway at a site, where cameras are on the local network).</summary>
        public static readonly Func<IPAddress, bool> Any = _ => true;

        /// <summary>Only addresses on the internet (a gateway in the cloud).</summary>
        public static readonly Func<IPAddress, bool> PublicOnly = IsPublic;

        /// <summary>
        /// The rule for this process; <see cref="Any"/> unless the host sets it once at startup. Process-wide on purpose:
        /// a gateway in the cloud runs alone in its process, and no camera connection may get around it.
        /// </summary>
        public static Func<IPAddress, bool> Allowed
        {
            get => Volatile.Read(ref _allowed);
            set => Volatile.Write(ref _allowed, value ?? Any);
        }

        private static Func<IPAddress, bool> _allowed = Any;

        public static bool IsRestricted => !ReferenceEquals(Allowed, Any);

        /// <summary>Whether <paramref name="address"/> is a unicast address on the internet.</summary>
        public static bool IsPublic(IPAddress address)
        {
            if (address == null)
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                return IsPublic(address.MapToIPv4());
            }

            var b = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                return !(b[0] == 0                                   // "this" network
                    || b[0] == 10                                    // private
                    || (b[0] == 100 && (b[1] & 0xC0) == 64)          // CGNAT 100.64/10
                    || b[0] == 127                                   // loopback
                    || (b[0] == 169 && b[1] == 254)                  // link-local, cloud metadata
                    || (b[0] == 172 && (b[1] & 0xF0) == 16)          // private 172.16/12
                    || (b[0] == 192 && b[1] == 0 && b[2] == 0)       // IETF protocol assignments
                    || (b[0] == 192 && b[1] == 0 && b[2] == 2)       // documentation
                    || (b[0] == 192 && b[1] == 88 && b[2] == 99)     // 6to4 relay anycast
                    || (b[0] == 192 && b[1] == 168)                  // private
                    || (b[0] == 198 && (b[1] & 0xFE) == 18)          // benchmarking 198.18/15
                    || (b[0] == 198 && b[1] == 51 && b[2] == 100)    // documentation
                    || (b[0] == 203 && b[1] == 0 && b[2] == 113)     // documentation
                    || b[0] >= 224);                                 // multicast, reserved, broadcast
            }

            if (address.AddressFamily != AddressFamily.InterNetworkV6)
            {
                return false;
            }

            if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback)
                || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                || (b[0] & 0xFE) == 0xFC                                                  // unique local fc00::/7
                || (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)         // documentation
                || (b[0] == 0x01 && b[1] == 0x00 && b.Skip(2).Take(6).All(x => x == 0)))  // discard 100::/64
            {
                return false;
            }

            // Addresses that carry an IPv4 address: judged by it (NAT64 64:ff9b::/96, 6to4 2002::/16, IPv4-compatible ::/96).
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b.Skip(4).Take(8).All(x => x == 0))
            {
                return IsPublic(new IPAddress(b.Skip(12).ToArray()));
            }

            if (b[0] == 0x20 && b[1] == 0x02)
            {
                return IsPublic(new IPAddress(b.Skip(2).Take(4).ToArray()));
            }

            if (b.Take(12).All(x => x == 0))
            {
                return false;
            }

            return true;
        }

        /// <summary>The addresses of <paramref name="host"/> this process may connect to; refused when there are none.</summary>
        public static async Task<IPAddress[]> ResolveAsync(string host, Func<IPAddress, bool> allowed, CancellationToken cancellationToken)
        {
            allowed ??= Allowed;
            var addresses = IPAddress.TryParse(host?.Trim('[', ']'), out var literal)
                ? new[] { literal }
                : await Dns.GetHostAddressesAsync(host ?? string.Empty, cancellationToken).ConfigureAwait(false);

            var usable = addresses.Where(a => allowed(a)).ToArray();
            if (usable.Length == 0 || (!ReferenceEquals(allowed, Any) && usable.Length != addresses.Length))
            {
                // A name with both kinds of address is refused too: which one is used is up to whoever runs its DNS.
                throw new CameraAddressRefusedException(host);
            }

            return usable;
        }

        /// <summary>Checks a camera URL's host (no connection is made); a relative or empty URL passes.</summary>
        public static async Task EnsureAllowedAsync(string url, Func<IPAddress, bool> allowed, CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(allowed ?? Allowed, Any) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                await ResolveAsync(uri.Host, allowed, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Opens a TCP connection to a camera at an address that passed the rule (the one checked is the one used).</summary>
        public static async Task<Socket> ConnectAsync(string host, int port, Func<IPAddress, bool> allowed, CancellationToken cancellationToken)
        {
            var addresses = await ResolveAsync(host, allowed, cancellationToken).ConfigureAwait(false);
            Exception last = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken).ConfigureAwait(false);
                    return socket;
                }
                catch (SocketException ex)
                {
                    socket.Dispose();
                    last = ex;
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }

            throw last ?? new SocketException((int)SocketError.HostNotFound);
        }

        /// <summary>An HTTP handler that connects only where the rule says.</summary>
        /// <param name="allowed">Null: <see cref="Allowed"/> at each connection.</param>
        public static SocketsHttpHandler CreateHttpHandler(Func<IPAddress, bool> allowed = null)
        {
            return new SocketsHttpHandler
            {
                ConnectCallback = async (context, cancellationToken) =>
                    new NetworkStream(await ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, allowed, cancellationToken).ConfigureAwait(false), ownsSocket: true)
            };
        }

        /// <summary>
        /// Checks each request's host before it is sent, for HTTP stacks whose connections cannot be opened here (WCF's
        /// own handler).
        /// </summary>
        public sealed class CheckingHandler : DelegatingHandler
        {
            public CheckingHandler(HttpMessageHandler inner)
                : base(inner)
            {
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (IsRestricted && request.RequestUri != null)
                {
                    await ResolveAsync(request.RequestUri.Host, null, cancellationToken).ConfigureAwait(false);
                }

                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
