// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Onvif.Open.Core.Discovery
{
    /// <summary>
    /// A device that answered a WS-Discovery probe.
    /// </summary>
    public sealed class DiscoveredDevice
    {
        #region Constructor

        public DiscoveredDevice(string endpointReference, IReadOnlyList<Uri> serviceAddresses, IReadOnlyList<string> types, IReadOnlyList<string> scopes, IPAddress respondedFrom)
        {
            EndpointReference = endpointReference;
            ServiceAddresses = serviceAddresses ?? Array.Empty<Uri>();
            Types = types ?? Array.Empty<string>();
            Scopes = scopes ?? Array.Empty<string>();
            RespondedFrom = respondedFrom;

            Name = ScopeValue("name");
            Hardware = ScopeValue("hardware");
            Location = ScopeValue("location");
            Profiles = ScopeValues("Profile");
            DeviceServiceUri = ChoosePreferredAddress();
        }

        #endregion

        #region Public Variables

        /// <summary>Stable device identity, usually "urn:uuid:...".</summary>
        public string EndpointReference { get; }

        /// <summary>Every device service address (XAddrs) the device announced.</summary>
        public IReadOnlyList<Uri> ServiceAddresses { get; }

        /// <summary>The address to use: an HTTP(S) XAddr, preferring the address the device answered from and IPv4.</summary>
        public Uri DeviceServiceUri { get; }

        public IReadOnlyList<string> Types { get; }

        /// <summary>Raw scope URIs, e.g. "onvif://www.onvif.org/name/FrontDoor".</summary>
        public IReadOnlyList<string> Scopes { get; }

        public string Name { get; }
        public string Hardware { get; }
        public string Location { get; }

        /// <summary>ONVIF profiles from the scopes, e.g. "Streaming" (Profile S), "T", "G".</summary>
        public IReadOnlyList<string> Profiles { get; }

        public IPAddress RespondedFrom { get; }

        #endregion

        #region Public Methods

        public override string ToString()
        {
            var name = string.IsNullOrEmpty(Name) ? EndpointReference : Name;
            return $"{name} ({Hardware}) {DeviceServiceUri}".Trim();
        }

        #endregion

        #region Private Methods

        private string ScopeValue(string category)
        {
            return ScopeValues(category).FirstOrDefault();
        }

        private IReadOnlyList<string> ScopeValues(string category)
        {
            var prefix = "onvif://www.onvif.org/" + category + "/";

            return Scopes
                .Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && s.Length > prefix.Length)
                .Select(s => Uri.UnescapeDataString(s.Substring(prefix.Length)))
                .ToList();
        }

        private Uri ChoosePreferredAddress()
        {
            return ServiceAddresses
                .Where(u => u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps)
                .OrderByDescending(u => RespondedFrom != null && IPAddress.TryParse(u.Host.Trim('[', ']'), out var host) && host.Equals(RespondedFrom))
                .ThenByDescending(u => IPAddress.TryParse(u.Host, out var host) && host.AddressFamily == AddressFamily.InterNetwork)
                .ThenByDescending(u => !u.Host.StartsWith("169.254.", StringComparison.Ordinal))
                .FirstOrDefault();
        }

        #endregion
    }
}
