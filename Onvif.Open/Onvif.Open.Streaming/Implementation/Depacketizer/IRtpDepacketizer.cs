// Sarthak Killedar
// 2026-11-07

using System.Collections.Generic;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Rtp;

namespace Onvif.Open.Streaming.Implementation.Depacketizer
{
    /// <summary>
    /// Strategy that turns RTP packets of one payload format into complete video frames.
    /// </summary>
    internal interface IRtpDepacketizer
    {
        #region Public Variables

        VideoCodec Codec { get; }

        #endregion

        #region Public Methods

        /// <summary>Consumes one packet and adds every frame it completes to <paramref name="frames"/>.</summary>
        void Process(RtpPacket packet, ICollection<VideoFrame> frames);

        #endregion
    }
}
