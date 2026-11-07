// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Core.Abstract.Interface.Discovery;
using Onvif.Open.Core.Discovery;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: WS-Discovery of ONVIF devices on the network.
    /// </summary>
    internal static class DiscoveryEndpoints
    {
        #region Public Methods

        public static IEndpointRouteBuilder MapDiscoveryApi(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/discovery", async (ICameraDiscovery discovery, RecorderOptions options, CancellationToken ct) =>
            {
                var devices = await discovery.FindAsync(ct);

                return devices
                    .OrderBy(d => d.DeviceServiceUri?.Host, StringComparer.Ordinal)
                    .Select(d => new
                    {
                        d.EndpointReference,
                        d.Name,
                        d.Hardware,
                        d.Location,
                        d.Profiles,
                        deviceServiceUri = d.DeviceServiceUri?.ToString(),
                        serviceAddresses = d.ServiceAddresses.Select(a => a.ToString()),
                        respondedFrom = d.RespondedFrom?.ToString(),
                        configuredCameraId = FindConfiguredCamera(options, d)
                    });
            });

            return app;
        }

        #endregion

        #region Private Methods

        /// <summary>The id of an already configured camera with the same host, so a UI can offer to add only new devices.</summary>
        private static string FindConfiguredCamera(RecorderOptions options, DiscoveredDevice device)
        {
            var hosts = device.ServiceAddresses.Select(a => a.Host).ToList();

            return options.Cameras.FirstOrDefault(c =>
                Uri.TryCreate(c.OnvifUrl ?? c.RtspUrl ?? string.Empty, UriKind.Absolute, out var configured) &&
                hosts.Contains(configured.Host, StringComparer.OrdinalIgnoreCase))?.Id;
        }

        #endregion
    }
}
