// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Net;
using System.Text;
using Onvif.Open.Discovery.Implementation.Protocol;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Discovery
{
    public class ProbeMatchParserTest
    {
        #region Private Variables

        private const string HikvisionStyle =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<env:Envelope xmlns:env=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:wsadis=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\" xmlns:tds=\"http://www.onvif.org/ver10/device/wsdl\">" +
            "<env:Header><wsadis:MessageID>urn:uuid:abc</wsadis:MessageID><wsadis:RelatesTo>urn:uuid:probe-1</wsadis:RelatesTo></env:Header>" +
            "<env:Body><d:ProbeMatches><d:ProbeMatch>" +
            "<wsadis:EndpointReference><wsadis:Address>urn:uuid:4d454930-0000-1000-8000-bcbac2a3c4d5</wsadis:Address></wsadis:EndpointReference>" +
            "<d:Types>dn:NetworkVideoTransmitter tds:Device</d:Types>" +
            "<d:Scopes>onvif://www.onvif.org/type/video_encoder onvif://www.onvif.org/Profile/Streaming onvif://www.onvif.org/Profile/T " +
            "onvif://www.onvif.org/name/Front%20Door onvif://www.onvif.org/hardware/DS-2CD2143G2-I onvif://www.onvif.org/location/city/hangzhou</d:Scopes>" +
            "<d:XAddrs>http://[fe80::bebc:c2ff:fea3:c4d5]/onvif/device_service http://192.168.1.64/onvif/device_service</d:XAddrs>" +
            "<d:MetadataVersion>10</d:MetadataVersion>" +
            "</d:ProbeMatch></d:ProbeMatches></env:Body></env:Envelope>";

        // A different WS-Discovery namespace version and prefixes, with whitespace-separated values on several lines.
        private const string OtherVendorStyle =
            "<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:a=\"http://www.w3.org/2005/08/addressing\" xmlns:wsd=\"http://docs.oasis-open.org/ws-dd/ns/discovery/2009/01\">" +
            "<s:Header><a:RelatesTo>urn:uuid:probe-2</a:RelatesTo></s:Header><s:Body><wsd:ProbeMatches><wsd:ProbeMatch>" +
            "<a:EndpointReference><a:Address>urn:uuid:aaaa</a:Address></a:EndpointReference>" +
            "<wsd:Scopes>\n onvif://www.onvif.org/name/AXIS_P3245\n onvif://www.onvif.org/hardware/P3245-LV \n</wsd:Scopes>" +
            "<wsd:XAddrs>http://169.254.10.10/onvif/device_service http://10.0.0.20:8080/onvif/services</wsd:XAddrs>" +
            "</wsd:ProbeMatch></wsd:ProbeMatches></s:Body></s:Envelope>";

        #endregion

        #region Public Methods

        [Fact]
        public void ShouldParseProbeMatchAndScopes()
        {
            var device = Assert.Single(ProbeMatchParser.Parse(Encoding.UTF8.GetBytes(HikvisionStyle), IPAddress.Parse("192.168.1.64"), out var relatesTo));

            Assert.Equal("urn:uuid:probe-1", relatesTo);
            Assert.Equal("urn:uuid:4d454930-0000-1000-8000-bcbac2a3c4d5", device.EndpointReference);
            Assert.Equal(new[] { "dn:NetworkVideoTransmitter", "tds:Device" }, device.Types);
            Assert.Equal("Front Door", device.Name);
            Assert.Equal("DS-2CD2143G2-I", device.Hardware);
            Assert.Equal("city/hangzhou", device.Location);
            Assert.Equal(new[] { "Streaming", "T" }, device.Profiles);
            Assert.Equal(2, device.ServiceAddresses.Count);

            // The address the device answered from wins over the IPv6 link-local one.
            Assert.Equal(new Uri("http://192.168.1.64/onvif/device_service"), device.DeviceServiceUri);
        }

        [Fact]
        public void ShouldToleratePrefixesNamespaceVersionsAndWhitespace()
        {
            var device = Assert.Single(ProbeMatchParser.Parse(Encoding.UTF8.GetBytes(OtherVendorStyle), IPAddress.Parse("10.0.0.20"), out var relatesTo));

            Assert.Equal("urn:uuid:probe-2", relatesTo);
            Assert.Equal("AXIS_P3245", device.Name);
            Assert.Equal("P3245-LV", device.Hardware);
            Assert.Empty(device.Types);
            Assert.Equal(new Uri("http://10.0.0.20:8080/onvif/services"), device.DeviceServiceUri);
        }

        [Fact]
        public void ShouldPreferRoutableIPv4WhenRespondingAddressIsUnknown()
        {
            var device = Assert.Single(ProbeMatchParser.Parse(Encoding.UTF8.GetBytes(OtherVendorStyle), null, out _));

            Assert.Equal("10.0.0.20", device.DeviceServiceUri.Host);
        }

        [Theory]
        [InlineData("not xml at all")]
        [InlineData("<s:Envelope xmlns:s=\"http://www.w3.org/2003/05/soap-envelope\"><s:Body><d:Probe xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\"/></s:Body></s:Envelope>")]
        [InlineData("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><x>&e;</x>")]
        public void NonMatchesShouldYieldNothing(string datagram)
        {
            Assert.Empty(ProbeMatchParser.Parse(Encoding.UTF8.GetBytes(datagram), IPAddress.Loopback, out _));
        }

        [Fact]
        public void ProbeShouldBeWellFormedWsDiscovery()
        {
            var probe = Encoding.UTF8.GetString(ProbeMessage.Create("urn:uuid:1", "dn:NetworkVideoTransmitter"));
            var document = System.Xml.Linq.XDocument.Parse(probe);

            Assert.Contains(document.Descendants(), e => e.Name.LocalName == "Probe" && e.Name.NamespaceName == "http://schemas.xmlsoap.org/ws/2005/04/discovery");
            Assert.Equal("urn:uuid:1", document.Descendants().Single(e => e.Name.LocalName == "MessageID").Value);
            Assert.Equal("dn:NetworkVideoTransmitter", document.Descendants().Single(e => e.Name.LocalName == "Types").Value);
        }

        #endregion
    }
}
