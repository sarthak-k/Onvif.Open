// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Streaming
{
    /// <summary>
    /// A video (<see cref="VideoFrame"/>) or audio (<see cref="AudioFrame"/>) frame. Frames of one stream share a
    /// timeline: <see cref="Timestamp"/> 0 is the first video frame.
    /// </summary>
    public abstract class MediaFrame
    {
        protected MediaFrame(ReadOnlyMemory<byte> data, uint rtpTimestamp, TimeSpan timestamp, DateTimeOffset? captureTime)
        {
            Data = data;
            RtpTimestamp = rtpTimestamp;
            Timestamp = timestamp;
            CaptureTime = captureTime;
        }

        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>RTP timestamp of the frame (in the clock rate of its track, 90 kHz for video).</summary>
        public uint RtpTimestamp { get; }

        /// <summary>Presentation time relative to the first video frame of the stream.</summary>
        public TimeSpan Timestamp { get; }

        /// <summary>
        /// Wall-clock time the frame was recorded, when the device reports it (ONVIF replay); otherwise null.
        /// </summary>
        public DateTimeOffset? CaptureTime { get; }
    }
}
