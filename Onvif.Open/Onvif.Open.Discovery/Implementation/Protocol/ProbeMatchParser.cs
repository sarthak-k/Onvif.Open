// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;
using System.Xml.Linq;
using Onvif.Open.Core.Discovery;

namespace Onvif.Open.Discovery.Implementation.Protocol
{
    /// <summary>
    /// Parses ProbeMatches responses. Matching is done on local names only, because devices use
    /// different prefixes and WS-Discovery/WS-Addressing namespace versions.
    /// </summary>
    internal static class ProbeMatchParser
    {
        #region Public Methods

        /// <summary>
        /// Parses <paramref name="datagram"/>. <paramref name="relatesTo"/> receives the MessageID the response answers (null when absent).
        /// Returns an empty list for anything that is not a well-formed ProbeMatches message.
        /// </summary>
        public static IReadOnlyList<DiscoveredDevice> Parse(byte[] datagram, IPAddress respondedFrom, out string relatesTo)
        {
            relatesTo = null;
            XDocument document;

            try
            {
                using (var stream = new MemoryStream(datagram))
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                {
                    document = XDocument.Load(reader);
                }
            }
            catch (XmlException)
            {
                return Array.Empty<DiscoveredDevice>();
            }

            relatesTo = Descendants(document, "RelatesTo").FirstOrDefault()?.Value.Trim();

            return Descendants(document, "ProbeMatch")
                .Select(match => CreateDevice(match, respondedFrom))
                .Where(device => device != null)
                .ToList();
        }

        #endregion

        #region Private Methods

        private static DiscoveredDevice CreateDevice(XElement match, IPAddress respondedFrom)
        {
            var endpoint = Child(match, "EndpointReference");
            var address = endpoint == null ? null : Child(endpoint, "Address")?.Value.Trim();

            var serviceAddresses = Split(Child(match, "XAddrs")?.Value)
                .Select(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) ? uri : null)
                .Where(uri => uri != null)
                .ToList();

            if (string.IsNullOrEmpty(address) && serviceAddresses.Count == 0)
            {
                return null;
            }

            var types = Split(Child(match, "Types")?.Value).ToList();
            var scopes = Split(Child(match, "Scopes")?.Value).ToList();

            return new DiscoveredDevice(address ?? serviceAddresses[0].ToString(), serviceAddresses, types, scopes, respondedFrom);
        }

        private static IEnumerable<XElement> Descendants(XContainer container, string localName)
        {
            return container.Descendants().Where(e => e.Name.LocalName == localName);
        }

        private static XElement Child(XElement element, string localName)
        {
            return element.Elements().FirstOrDefault(e => e.Name.LocalName == localName);
        }

        private static IEnumerable<string> Split(string value)
        {
            return (value ?? string.Empty).Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        #endregion
    }
}
