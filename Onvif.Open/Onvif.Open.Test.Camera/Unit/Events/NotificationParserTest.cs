// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Xml;
using Onvif.Open.Core.Events;
using Onvif.Open.Events.Implementation.Client;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Events
{
    public class NotificationParserTest
    {
        #region Private Methods

        private static XmlElement Element(string xml)
        {
            var document = new XmlDocument();
            document.LoadXml(xml.Replace("<tt:", "<tt:").Contains("xmlns:tt") ? xml : xml.Replace("<wsnt:NotificationMessage ", "<wsnt:NotificationMessage xmlns:tt=\"http://www.onvif.org/ver10/schema\" "));
            return document.DocumentElement;
        }

        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

        #endregion

        #region Public Methods

        [Fact]
        public void ShouldParseMotionEvent()
        {
            var cameraEvent = Assert.Single(NotificationParser.Parse(new[] { Element(FakeOnvifServer.MotionNotification(true)) }, "cam1", Now));

            Assert.Equal("cam1", cameraEvent.CameraId);
            Assert.Equal("tns1:RuleEngine/CellMotionDetector/Motion", cameraEvent.Topic);
            Assert.Equal("RuleEngine/CellMotionDetector/Motion", cameraEvent.TopicPath);
            Assert.Equal(new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero), cameraEvent.UtcTime);
            Assert.Equal("Changed", cameraEvent.PropertyOperation);
            Assert.False(cameraEvent.IsInitialState);
            Assert.Equal("VSC_1", cameraEvent.Source["VideoSourceConfigurationToken"]);
            Assert.Equal("true", cameraEvent.Data["IsMotion"]);
            Assert.True(cameraEvent.GetState());
            Assert.True(cameraEvent.MatchesTopic("RuleEngine/CellMotionDetector"));
            Assert.True(cameraEvent.MatchesTopic("tns1:RuleEngine/CellMotionDetector/Motion"));
            Assert.False(cameraEvent.MatchesTopic("RuleEngine/Cell"));
        }

        [Fact]
        public void ShouldParseOtherVendorPrefixesAndDigitalInputs()
        {
            const string xml =
                "<ns2:NotificationMessage xmlns:ns2=\"http://docs.oasis-open.org/wsn/b-2\" xmlns:ns5=\"http://www.onvif.org/ver10/schema\" xmlns:tnsaxis=\"http://www.axis.com/2009/event/topics\">" +
                "<ns2:Topic Dialect=\"http://docs.oasis-open.org/wsn/t-1/TopicExpression/Simple\">tns1:Device/tnsaxis:IO/Port</ns2:Topic>" +
                "<ns2:Message><ns5:Message UtcTime=\"bad-time\" PropertyOperation=\"Initialized\">" +
                "<ns5:Source><ns5:SimpleItem Name=\"port\" Value=\"1\"/></ns5:Source>" +
                "<ns5:Data><ns5:SimpleItem Name=\"state\" Value=\"0\"/></ns5:Data>" +
                "</ns5:Message></ns2:Message></ns2:NotificationMessage>";

            var cameraEvent = Assert.Single(NotificationParser.Parse(new[] { Element(xml) }, null, Now));

            Assert.Equal("Device/IO/Port", cameraEvent.TopicPath);
            Assert.True(cameraEvent.IsInitialState);
            Assert.Equal(Now, cameraEvent.UtcTime);
            Assert.False(cameraEvent.GetState());
            Assert.Equal("1", cameraEvent.Source["port"]);
        }

        [Fact]
        public void NonNotificationsAndIncompleteMessagesShouldBeIgnored()
        {
            var elements = new[]
            {
                Element("<tev:CurrentTime xmlns:tev=\"http://www.onvif.org/ver10/events/wsdl\">2026-11-07T10:00:00Z</tev:CurrentTime>"),
                Element("<wsnt:NotificationMessage xmlns:wsnt=\"http://docs.oasis-open.org/wsn/b-2\"><wsnt:Topic>tns1:X</wsnt:Topic></wsnt:NotificationMessage>")
            };

            Assert.Empty(NotificationParser.Parse(elements, null, Now));
        }

        [Fact]
        public void EventWithoutBooleanDataHasNoState()
        {
            var cameraEvent = new CameraEvent("cam", "tns1:Monitoring/ProcessorUsage", Now, "Changed", null, null,
                new System.Collections.Generic.Dictionary<string, string> { ["Value"] = "37.5" });

            Assert.Null(cameraEvent.GetState());
        }

        [Fact]
        public void EndpointReferenceShouldYieldAddressAndReferenceParameters()
        {
            var reference = Element(
                "<tev:SubscriptionReference xmlns:tev=\"http://www.onvif.org/ver10/events/wsdl\" xmlns:a=\"http://www.w3.org/2005/08/addressing\">" +
                "<a:Address>http://10.0.0.5/onvif/Events/PullSubManager_1</a:Address>" +
                "<a:ReferenceParameters><dom0:SubscriptionId xmlns:dom0=\"http://www.axis.com/2009/event\">42</dom0:SubscriptionId></a:ReferenceParameters>" +
                "</tev:SubscriptionReference>");

            var (address, parameters) = NotificationParser.ParseEndpointReference(reference);

            Assert.Equal(new Uri("http://10.0.0.5/onvif/Events/PullSubManager_1"), address);
            Assert.Equal("SubscriptionId", Assert.Single(parameters).LocalName);
            Assert.Throws<InvalidOperationException>(() => NotificationParser.ParseEndpointReference(Element("<x/>")));
        }

        #endregion
    }
}
