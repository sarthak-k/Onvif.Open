// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Sdp;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// Strategy of an <see cref="RtspSession{TFrame}"/>: which track to play and how its RTP packets become frames
    /// (video access units, analytics metadata documents, ...).
    /// </summary>
    internal interface IRtpTrackReader<TFrame>
    {
        #region Public Methods

        /// <summary>The track to set up; throws when the stream does not have one.</summary>
        SdpMediaDescription SelectTrack(SessionDescription sessionDescription);

        /// <summary>Consumes one packet and adds every frame it completes to <paramref name="frames"/>.</summary>
        void Process(RtpPacket packet, ICollection<TFrame> frames);

        uint GetRtpTimestamp(TFrame frame);

        DateTimeOffset? GetCaptureTime(TFrame frame);

        /// <summary>The frame with its recording time (ONVIF replay).</summary>
        TFrame WithCaptureTime(TFrame frame, DateTimeOffset captureTime);

        #endregion
    }
}
