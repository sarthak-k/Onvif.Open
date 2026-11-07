// Sarthak Killedar
// 2026-11-07

using System.Collections.Generic;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Rtp;

namespace Onvif.Open.Streaming.Implementation.Depacketizer.Audio
{
    /// <summary>
    /// G.711 (RFC 3551): each packet carries one sample per byte, 8 kHz.
    /// </summary>
    internal sealed class G711Depacketizer : IAudioDepacketizer
    {
        #region Constructor

        public G711Depacketizer(AudioCodec codec, int sampleRate, int channels)
        {
            Format = new AudioFormat(codec, sampleRate, channels);
            _clock = new RtpTimestampClock(sampleRate);
        }

        #endregion

        #region Private Variables

        private readonly RtpTimestampClock _clock;

        #endregion

        #region Public Variables

        public AudioFormat Format { get; }

        #endregion

        #region Public Methods

        public void Process(RtpPacket packet, ICollection<AudioFrame> frames)
        {
            if (packet.Payload.IsEmpty)
            {
                return;
            }

            frames.Add(new AudioFrame(Format, packet.Payload.ToArray(), packet.Timestamp, _clock.ToTime(packet.Timestamp)));
        }

        #endregion
    }
}
