// Sarthak Killedar
// 2026-11-07

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Discovery;

namespace Onvif.Open.Core.Abstract.Interface.Discovery
{
    /// <summary>
    /// Finds ONVIF devices with WS-Discovery (multicast probes on the local network, plus optional unicast targets).
    /// </summary>
    public interface ICameraDiscovery
    {
        #region Public Methods

        /// <summary>Yields devices as they answer; completes when the discovery timeout elapses.</summary>
        IAsyncEnumerable<DiscoveredDevice> DiscoverAsync(CancellationToken cancellationToken = default);

        /// <summary>Probes and returns every device that answered within the discovery timeout.</summary>
        Task<IReadOnlyList<DiscoveredDevice>> FindAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
