// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Depacketizer;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Sdp;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// Reads the video track: H.264/H.265 (or generic) depacketizing into <see cref="VideoFrame"/>s.
    /// </summary>
    internal sealed class VideoTrackReader : IRtpTrackReader<VideoFrame>
    {
        #region Private Variables

        private IRtpDepacketizer _depacketizer;

        #endregion

        #region Public Variables

        public VideoCodec Codec => _depacketizer?.Codec ?? VideoCodec.Unknown;

        #endregion

        #region Public Methods

        public SdpMediaDescription SelectTrack(SessionDescription sessionDescription)
        {
            var video = sessionDescription.Video ?? throw new InvalidOperationException("The RTSP stream does not contain a video track.");
            _depacketizer = RtpDepacketizerFactory.Create(video);
            return video;
        }

        public void Process(RtpPacket packet, ICollection<VideoFrame> frames)
        {
            _depacketizer.Process(packet, frames);
        }

        public uint GetRtpTimestamp(VideoFrame frame)
        {
            return frame.RtpTimestamp;
        }

        public DateTimeOffset? GetCaptureTime(VideoFrame frame)
        {
            return frame.CaptureTime;
        }

        public VideoFrame WithCaptureTime(VideoFrame frame, DateTimeOffset captureTime)
        {
            return new VideoFrame(frame.Codec, frame.Data, frame.IsKeyFrame, frame.RtpTimestamp, frame.Timestamp, captureTime);
        }

        #endregion
    }
}
