// Sarthak Killedar
// 2026-11-07

using System;
using System.Text;

namespace Onvif.Open.Discovery.Implementation.Protocol
{
    /// <summary>
    /// WS-Discovery (2005/04, as used by ONVIF) Probe messages.
    /// </summary>
    internal static class ProbeMessage
    {
        #region Public Variables

        public const int Port = 3702;
        public const string MulticastAddress = "239.255.255.250";

        /// <summary>
        /// Probed device types. Most devices answer NetworkVideoTransmitter; some only answer a Device probe.
        /// </summary>
        public static readonly string[] DeviceTypes =
        {
            "dn:NetworkVideoTransmitter",
            "tds:Device"
        };

        #endregion

        #region Public Methods

        public static string NewMessageId()
        {
            return "urn:uuid:" + Guid.NewGuid().ToString("D");
        }

        public static byte[] Create(string messageId, string type)
        {
            var xml =
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" " +
                "xmlns:a=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" " +
                "xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" " +
                "xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\" " +
                "xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\">" +
                "<s:Header>" +
                "<a:Action s:mustUnderstand=\"1\">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</a:Action>" +
                $"<a:MessageID>{messageId}</a:MessageID>" +
                "<a:ReplyTo><a:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</a:Address></a:ReplyTo>" +
                "<a:To s:mustUnderstand=\"1\">urn:schemas-xmlsoap-org:ws:2005:04:discovery</a:To>" +
                "</s:Header>" +
                $"<s:Body><d:Probe><d:Types>{type}</d:Types></d:Probe></s:Body>" +
                "</s:Envelope>";

            return Encoding.UTF8.GetBytes(xml);
        }

        #endregion
    }
}
