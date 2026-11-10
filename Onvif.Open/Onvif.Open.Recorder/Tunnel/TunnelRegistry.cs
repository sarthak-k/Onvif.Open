// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Concurrent;

namespace Onvif.Open.Recorder.Tunnel
{
    /// <summary>The control plane's open tunnels, by gateway: at most one per gateway (a new one replaces the old).</summary>
    public sealed class TunnelRegistry
    {
        private readonly ConcurrentDictionary<string, TunnelConnection> _tunnels = new ConcurrentDictionary<string, TunnelConnection>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A gateway's tunnel opened: its cameras are sent to it.</summary>
        public event Action<string> Connected;

        public TunnelConnection Find(string gatewayId) =>
            gatewayId != null && _tunnels.TryGetValue(gatewayId, out var tunnel) && tunnel.IsOpen ? tunnel : null;

        public bool IsConnected(string gatewayId) => Find(gatewayId) != null;

        /// <summary>Registers a gateway's tunnel; returns the one it replaces (to close), if any.</summary>
        public TunnelConnection Add(string gatewayId, TunnelConnection tunnel)
        {
            TunnelConnection previous = null;
            _tunnels.AddOrUpdate(gatewayId, tunnel, (_, old) =>
            {
                previous = old;
                return tunnel;
            });
            Connected?.Invoke(gatewayId);
            return previous;
        }

        public void Remove(string gatewayId, TunnelConnection tunnel) =>
            _tunnels.TryRemove(new System.Collections.Generic.KeyValuePair<string, TunnelConnection>(gatewayId, tunnel));
    }
}
