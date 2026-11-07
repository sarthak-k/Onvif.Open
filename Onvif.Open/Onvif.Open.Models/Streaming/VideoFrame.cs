// Sarthak Killedar
using System;

namespace Onvif.Open.Core.Streaming
{
    /// <summary>
    /// One encoded video frame. For H.264/H.265 <see cref="Data"/> is an access unit in Annex-B
    /// format (each NAL unit prefixed with a 00 00 00 01 start code). For other codecs it is the
    /// concatenated RTP payload of the frame.
    /// </summary>
    public sealed class VideoFrame
    {
        public VideoFrame(VideoCodec codec, ReadOnlyMemory<byte> data, bool isKeyFrame, uint rtpTimestamp, TimeSpan timestamp, DateTimeOffset? captureTime = null)
        {
            CaptureTime = captureTime;
            Codec = codec;
            Data = data;
            IsKeyFrame = isKeyFrame;
            RtpTimestamp = rtpTimestamp;
            Timestamp = timestamp;
        }

        public VideoCodec Codec { get; }
        public ReadOnlyMemory<byte> Data { get; }
        public bool IsKeyFrame { get; }

        /// <summary>RTP timestamp of the frame (90 kHz clock for video).</summary>
        public uint RtpTimestamp { get; }

        /// <summary>Presentation time relative to the first frame of the stream.</summary>
        public TimeSpan Timestamp { get; }

        /// <summary>
        /// Wall-clock time the frame was recorded, when the device reports it (ONVIF replay); otherwise null.
        /// </summary>
        public DateTimeOffset? CaptureTime { get; }
    }
}
