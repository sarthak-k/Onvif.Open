// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Sdp;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// Strategy of an <see cref="RtspSession{TFrame}"/>: which tracks to play and how their RTP packets become frames
    /// (video and audio, analytics metadata documents, ...).
    /// </summary>
    internal interface IRtpTrackReader<TFrame>
    {
        #region Public Methods

        /// <summary>The tracks to set up, in order (their index is passed to <see cref="Process"/>); throws when a required one is missing.</summary>
        IReadOnlyList<SdpMediaDescription> SelectTracks(SessionDescription sessionDescription);

        /// <summary>Consumes one packet of track <paramref name="track"/> and adds every frame it completes to <paramref name="frames"/>.</summary>
        void Process(int track, RtpPacket packet, ICollection<TFrame> frames);

        /// <summary>Recording time of a frame (ONVIF replay), used to stop a replay at its end time.</summary>
        DateTimeOffset? GetCaptureTime(TFrame frame);

        #endregion
    }
}
