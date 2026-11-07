// Sarthak Killedar
// 2026-11-07

using System.Collections.Generic;
using System.IO;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Rtp;

namespace Onvif.Open.Streaming.Implementation.Depacketizer
{
    /// <summary>
    /// Fallback for payload formats without a dedicated depacketizer: concatenates the RTP
    /// payloads that share a timestamp into one frame.
    /// </summary>
    internal sealed class GenericDepacketizer : IRtpDepacketizer
    {
        #region Constructor

        public GenericDepacketizer(int clockRate)
        {
            _clock = new RtpTimestampClock(clockRate);
        }

        #endregion

        #region Private Variables

        private readonly RtpTimestampClock _clock;
        private readonly MemoryStream _buffer = new MemoryStream();
        private bool _hasTimestamp;
        private uint _timestamp;

        #endregion

        #region Public Variables

        public VideoCodec Codec => VideoCodec.Unknown;

        #endregion

        #region Public Methods

        public void Process(RtpPacket packet, ICollection<VideoFrame> frames)
        {
            if (_hasTimestamp && packet.Timestamp != _timestamp)
            {
                Flush(frames);
            }

            _hasTimestamp = true;
            _timestamp = packet.Timestamp;
            _buffer.Write(packet.Payload.Span);

            if (packet.Marker)
            {
                Flush(frames);
            }
        }

        #endregion

        #region Private Methods

        private void Flush(ICollection<VideoFrame> frames)
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            frames.Add(new VideoFrame(Codec, _buffer.ToArray(), false, _timestamp, _clock.ToTime(_timestamp)));
            _buffer.SetLength(0);
        }

        #endregion
    }
}
