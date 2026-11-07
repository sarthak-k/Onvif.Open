// Sarthak Killedar
using System;

namespace Onvif.Open.Core.Streaming
{
    /// <summary>
    /// One encoded video frame. For H.264/H.265 <see cref="MediaFrame.Data"/> is an access unit in Annex-B
    /// format (each NAL unit prefixed with a 00 00 00 01 start code). For other codecs it is the
    /// concatenated RTP payload of the frame.
    /// </summary>
    public sealed class VideoFrame : MediaFrame
    {
        public VideoFrame(VideoCodec codec, ReadOnlyMemory<byte> data, bool isKeyFrame, uint rtpTimestamp, TimeSpan timestamp, DateTimeOffset? captureTime = null)
            : base(data, rtpTimestamp, timestamp, captureTime)
        {
            Codec = codec;
            IsKeyFrame = isKeyFrame;
        }

        public VideoCodec Codec { get; }
        public bool IsKeyFrame { get; }
    }
}
