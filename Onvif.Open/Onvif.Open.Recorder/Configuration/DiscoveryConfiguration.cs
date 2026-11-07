// Sarthak Killedar
// 2026-11-07

using System;
using System.Net;
using Onvif.Open.Core.Abstract.Interface.Discovery;
using Onvif.Open.Discovery.Implementation.Builder;

namespace Onvif.Open.Recorder.Configuration
{
    public static class DiscoveryConfiguration
    {
        #region Public Methods

        public static ICameraDiscovery CreateDiscovery(DiscoveryOptions options)
        {
            options = options ?? new DiscoveryOptions();

            var builder = CameraDiscoveryBuilder.GetBuilder()
                .SetTimeout(options.Timeout)
                .SetMulticast(options.Multicast);

            foreach (var target in options.Targets)
            {
                if (!IPEndPoint.TryParse(target ?? string.Empty, out var endpoint))
                {
                    throw new InvalidOperationException($"Invalid discovery target '{target}': use an IP address, optionally with a port.");
                }

                builder.AddTarget(endpoint.Address, endpoint.Port == 0 ? 3702 : endpoint.Port);
            }

            return builder.Build();
        }

        #endregion
    }
}
