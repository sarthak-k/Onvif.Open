// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Streaming.Implementation.Metadata;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Rtsp;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Streaming
{
    public class AnalyticsMetadataTest
    {
        #region Private Variables

        /// <summary>Profile M style: tt:Type with a Likelihood attribute, no transformation (-1..1, y up).</summary>
        internal const string ProfileMDocument =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<tt:MetadataStream xmlns:tt=\"http://www.onvif.org/ver10/schema\"><tt:VideoAnalytics>" +
            "<tt:Frame UtcTime=\"2026-11-07T10:00:00.250Z\" Source=\"VS_1\">" +
            "<tt:Object ObjectId=\"12\"><tt:Appearance><tt:Shape>" +
            "<tt:BoundingBox left=\"-0.5\" top=\"0.5\" right=\"0.0\" bottom=\"-0.5\"/><tt:CenterOfGravity x=\"-0.25\" y=\"0\"/></tt:Shape>" +
            "<tt:Class><tt:Type Likelihood=\"0.35\">Vehicle</tt:Type><tt:Type Likelihood=\"0.92\">Human</tt:Type></tt:Class>" +
            "</tt:Appearance></tt:Object>" +
            "<tt:Object ObjectId=\"13\"><tt:Appearance><tt:Class><tt:Type>Face</tt:Type></tt:Class></tt:Appearance></tt:Object>" +
            "</tt:Frame></tt:VideoAnalytics></tt:MetadataStream>";

        #endregion

        #region Public Methods

        [Fact]
        public void ProfileMObjectsShouldBeParsedWithImageFractions()
        {
            var frame = Assert.Single(AnalyticsMetadataParser.Parse(ProfileMDocument));

            Assert.Equal(new DateTimeOffset(2026, 11, 7, 10, 0, 0, 250, TimeSpan.Zero), frame.UtcTime);
            Assert.Equal("VS_1", frame.Source);
            Assert.Equal(2, frame.Objects.Count);

            var person = frame.Objects[0];
            Assert.Equal("12", person.ObjectId);
            Assert.Equal("Human", person.Class);
            Assert.Equal(0.92, person.Likelihood);
            Assert.Equal(new[] { "Human", "Vehicle" }, person.Classes.Select(c => c.Type));

            // -0.5..0 horizontally, +0.5..-0.5 vertically (y up) -> 0.25..0.5 and 0.25..0.75 of the picture.
            Assert.Equal(0.25, person.BoundingBox.Left, 6);
            Assert.Equal(0.5, person.BoundingBox.Right, 6);
            Assert.Equal(0.25, person.BoundingBox.Top, 6);
            Assert.Equal(0.75, person.BoundingBox.Bottom, 6);
            Assert.Equal((480, 270, 480, 540), person.BoundingBox.ToPixels(1920, 1080));

            Assert.Null(frame.Objects[1].BoundingBox);
            Assert.Null(frame.Objects[1].Likelihood);

            Assert.True(frame.Contains("human", 0.9));
            Assert.False(frame.Contains("Vehicle", 0.5));
            Assert.True(frame.Contains("Face", 0.99));
        }

        [Fact]
        public void TransformationAndClassCandidatesShouldBeApplied()
        {
            // Pixel coordinates of a 640x360 picture: Translate -1/-1, Scale 2/640 and 2/360 (y down, flipped by scale -2/360 and translate +1).
            const string document =
                "<MetadataStream xmlns=\"http://www.onvif.org/ver10/schema\"><VideoAnalytics>" +
                "<Frame UtcTime=\"2026-11-07T10:00:01Z\"><Transformation><Translate x=\"-1\" y=\"1\"/><Scale x=\"0.003125\" y=\"-0.0055555556\"/></Transformation>" +
                "<Object ObjectId=\"7\"><Appearance><Shape><BoundingBox left=\"64\" top=\"36\" right=\"320\" bottom=\"180\"/></Shape>" +
                "<Class><ClassCandidate><Type>Vehical</Type><Likelihood>0.7</Likelihood></ClassCandidate></Class></Appearance></Object>" +
                "</Frame><Frame UtcTime=\"2026-11-07T10:00:02Z\"/></VideoAnalytics></MetadataStream>";

            var frames = AnalyticsMetadataParser.Parse(document);

            Assert.Equal(2, frames.Count);
            var box = frames[0].Objects.Single().BoundingBox;
            Assert.Equal(0.1, box.Left, 4);
            Assert.Equal(0.1, box.Top, 4);
            Assert.Equal(0.5, box.Right, 4);
            Assert.Equal(0.5, box.Bottom, 4);
            Assert.Equal("Vehical", frames[0].Objects[0].Class);
            Assert.Equal(0.7, frames[0].Objects[0].Likelihood);
            Assert.Empty(frames[1].Objects);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not xml")]
        [InlineData("<tt:MetadataStream xmlns:tt=\"http://www.onvif.org/ver10/schema\"><tt:Event/></tt:MetadataStream>")]
        [InlineData("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><MetadataStream><VideoAnalytics><Frame UtcTime=\"2026-11-07T10:00:00Z\">&e;</Frame></VideoAnalytics></MetadataStream>")]
        public void DocumentsWithoutAnalyticsOrInvalidOnesShouldYieldNothing(string document)
        {
            Assert.Empty(AnalyticsMetadataParser.Parse(document));
        }

        [Fact]
        public void TrackReaderShouldJoinPacketsUntilTheMarker()
        {
            var reader = new MetadataTrackReader();
            var frames = new List<AnalyticsFrame>();
            var bytes = Encoding.UTF8.GetBytes(ProfileMDocument);
            var half = bytes.Length / 2;

            reader.Process(Packet(1, 9000, false, bytes.Take(half).ToArray()), frames);
            Assert.Empty(frames);
            reader.Process(Packet(2, 9000, true, bytes.Skip(half).ToArray()), frames);
            Assert.Single(frames);

            // A document whose end was lost is dropped when the next one starts.
            reader.Process(Packet(3, 18000, false, bytes.Take(half).ToArray()), frames);
            reader.Process(Packet(4, 27000, true, bytes), frames);
            Assert.Equal(2, frames.Count);
        }

        [Fact]
        public void OversizedDocumentsShouldBeDropped()
        {
            var reader = new MetadataTrackReader();
            var frames = new List<AnalyticsFrame>();
            var chunk = new byte[1000];

            for (ushort i = 0; i < 1100; i++)
            {
                reader.Process(Packet(i, 9000, false, chunk), frames);
            }

            reader.Process(Packet(1100, 9000, true, Encoding.UTF8.GetBytes(ProfileMDocument)), frames);
            Assert.Empty(frames);

            reader.Process(Packet(1101, 18000, true, Encoding.UTF8.GetBytes(ProfileMDocument)), frames);
            Assert.Single(frames);
        }

        #endregion

        #region Private Methods

        private static RtpPacket Packet(ushort sequence, uint timestamp, bool marker, byte[] payload)
        {
            Assert.True(RtpPacket.TryParse(RtpPacketFactory.Create(sequence, timestamp, marker, payload, 107), out var packet));
            return packet;
        }

        #endregion
    }
}
