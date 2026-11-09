// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Discovery;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Discovery;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>
    /// The gateway's storage limits as a retention policy, read at every retention run: a change applies at the next.
    /// </summary>
    public sealed class GatewayRetention : IRetentionPolicy
    {
        public GatewayRetention(Func<StorageLimits> limits)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
        }

        private readonly Func<StorageLimits> _limits;

        public IEnumerable<RecordingSegment> SelectForDeletion(RetentionContext context) =>
            RecordingConfiguration.ToRetentionPolicy(GatewayService.ToRetentionOptions(_limits())).SelectForDeletion(context);
    }

    /// <summary>
    /// Discovery with the gateway's current settings (multicast, addresses); the timeout comes from "Discovery:Timeout".
    /// </summary>
    public sealed class GatewayDiscovery : ICameraDiscovery
    {
        public GatewayDiscovery(Func<DiscoverySettings> settings, TimeSpan timeout)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _timeout = timeout;
        }

        private readonly Func<DiscoverySettings> _settings;
        private readonly TimeSpan _timeout;

        public IAsyncEnumerable<DiscoveredDevice> DiscoverAsync(CancellationToken cancellationToken = default) => Create().DiscoverAsync(cancellationToken);

        public Task<IReadOnlyList<DiscoveredDevice>> FindAsync(CancellationToken cancellationToken = default) => Create().FindAsync(cancellationToken);

        private ICameraDiscovery Create()
        {
            var settings = _settings();
            return DiscoveryConfiguration.CreateDiscovery(new DiscoveryOptions { Timeout = _timeout, Multicast = settings.Multicast, Targets = settings.Addresses ?? new List<string>() });
        }
    }
}
