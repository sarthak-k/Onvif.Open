// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using Onvif.Open.Core.Events;

namespace Onvif.Open.Events.Implementation.Client
{
    /// <summary>
    /// Parses wsnt:NotificationMessage elements carrying ONVIF tt:Message payloads (by local names).
    /// </summary>
    internal static class NotificationParser
    {
        #region Public Methods

        public static IReadOnlyList<CameraEvent> Parse(IEnumerable<XmlElement> elements, string cameraId, DateTimeOffset now)
        {
            return (elements ?? Enumerable.Empty<XmlElement>())
                .Where(e => e?.LocalName == "NotificationMessage")
                .Select(e => ParseNotification(e, cameraId, now))
                .Where(e => e != null)
                .ToList();
        }

        /// <summary>The address and reference parameters of a wsa:EndpointReference-like element.</summary>
        public static (Uri Address, IReadOnlyList<XmlElement> ReferenceParameters) ParseEndpointReference(XmlElement reference)
        {
            return ParseEndpointReference(reference?.ChildNodes.OfType<XmlElement>());
        }

        /// <summary>Same as above, from the children of the endpoint reference.</summary>
        public static (Uri Address, IReadOnlyList<XmlElement> ReferenceParameters) ParseEndpointReference(IEnumerable<XmlElement> children)
        {
            var list = (children ?? Enumerable.Empty<XmlElement>()).ToList();
            var addressText = list.FirstOrDefault(e => e.LocalName == "Address")?.InnerText?.Trim();

            if (string.IsNullOrEmpty(addressText) || !Uri.TryCreate(addressText, UriKind.Absolute, out var address))
            {
                throw new InvalidOperationException("The device returned a subscription reference without a valid address.");
            }

            var parameters = list.FirstOrDefault(e => e.LocalName == "ReferenceParameters")?.ChildNodes.OfType<XmlElement>().ToList() ?? new List<XmlElement>();
            return (address, parameters);
        }

        #endregion

        #region Private Methods

        private static CameraEvent ParseNotification(XmlElement notification, string cameraId, DateTimeOffset now)
        {
            var topic = Child(notification, "Topic")?.InnerText?.Trim();
            var message = Child(Child(notification, "Message"), "Message") ?? Child(notification, "Message");

            if (string.IsNullOrEmpty(topic) || message == null)
            {
                return null;
            }

            var utcTime = DateTimeOffset.TryParse(message.GetAttribute("UtcTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : now;

            var operation = message.GetAttribute("PropertyOperation");

            return new CameraEvent(
                cameraId,
                topic,
                utcTime,
                string.IsNullOrEmpty(operation) ? null : operation,
                SimpleItems(Child(message, "Source")),
                SimpleItems(Child(message, "Key")),
                SimpleItems(Child(message, "Data")));
        }

        private static IReadOnlyDictionary<string, string> SimpleItems(XmlElement container)
        {
            var items = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (container == null)
            {
                return items;
            }

            foreach (var item in container.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "SimpleItem"))
            {
                var name = item.GetAttribute("Name");
                if (!string.IsNullOrEmpty(name))
                {
                    items[name] = item.GetAttribute("Value");
                }
            }

            return items;
        }

        private static XmlElement Child(XmlElement element, string localName)
        {
            return element?.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == localName);
        }

        #endregion
    }
}
