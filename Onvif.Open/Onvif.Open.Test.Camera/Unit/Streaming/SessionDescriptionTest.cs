// Sarthak Killedar
// 2026-11-07

using Onvif.Open.Streaming.Implementation.Rtsp;
using Onvif.Open.Streaming.Implementation.Sdp;
using Onvif.Open.Streaming.Implementation.Transport;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Streaming
{
    public class SessionDescriptionTest
    {
        #region Public Methods

        [Fact]
        public void ShouldParseVideoTrack()
        {
            var sdp = SessionDescription.Parse(new FakeRtspServerSdp().Sdp);

            Assert.Equal("*", sdp.Control);
            Assert.Equal(2, sdp.Media.Count);
            Assert.Equal("H264", sdp.Video.EncodingName);
            Assert.Equal(90000, sdp.Video.ClockRate);
            Assert.Equal(96, sdp.Video.PayloadType);
            Assert.Equal("trackID=1", sdp.Video.Control);
            Assert.Equal("1", sdp.Video.FormatParameters["packetization-mode"]);
            Assert.True(sdp.Video.FormatParameters.ContainsKey("sprop-parameter-sets"));
        }

        [Theory]
        [InlineData("rtsp://cam/stream/", "trackID=1", "rtsp://cam/stream/trackID=1")]
        [InlineData("rtsp://cam/stream", "trackID=1", "rtsp://cam/stream/trackID=1")]
        [InlineData("rtsp://cam/stream", "*", "rtsp://cam/stream")]
        [InlineData("rtsp://cam/stream", null, "rtsp://cam/stream")]
        [InlineData("rtsp://cam/stream", "rtsp://other/track1", "rtsp://other/track1")]
        public void ShouldResolveControlUrl(string baseUri, string control, string expected)
        {
            Assert.Equal(expected, RtspLiveVideoStream.ResolveControl(baseUri, control));
        }

        [Theory]
        [InlineData("RTP/AVP/TCP;unicast;interleaved=0-1", 0)]
        [InlineData("RTP/AVP/TCP;unicast;interleaved=2-3;ssrc=1234", 2)]
        [InlineData(null, 0)]
        public void ShouldParseInterleavedChannel(string transport, int expected)
        {
            var tcp = new TcpInterleavedRtpTransport();
            tcp.Configure(transport);
            Assert.Equal(expected, tcp.RtpChannel);
        }

        #endregion

        private sealed class FakeRtspServerSdp
        {
            public string Sdp =>
                "v=0\r\ns=Fake\r\na=control:*\r\n" +
                "m=video 0 RTP/AVP 96\r\na=rtpmap:96 H264/90000\r\n" +
                "a=fmtp:96 packetization-mode=1;sprop-parameter-sets=" + System.Convert.ToBase64String(FakeRtspServer.Sps) + "," + System.Convert.ToBase64String(FakeRtspServer.Pps) + "\r\n" +
                "a=control:trackID=1\r\n" +
                "m=audio 0 RTP/AVP 0\r\na=control:trackID=2\r\n";
        }
    }
}
