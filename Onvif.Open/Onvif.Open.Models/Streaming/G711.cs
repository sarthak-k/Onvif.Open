// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Streaming
{
    /// <summary>
    /// Decodes G.711 (ITU-T) audio, the default codec of most cameras, to 16-bit linear PCM, e.g. for audio analytics.
    /// </summary>
    public static class G711
    {
        #region Public Methods

        /// <summary>16-bit PCM samples of a <see cref="AudioCodec.Pcmu"/> or <see cref="AudioCodec.Pcma"/> frame.</summary>
        public static short[] DecodeToPcm16(AudioFrame frame)
        {
            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            switch (frame.Codec)
            {
                case AudioCodec.Pcmu:
                    return Decode(frame.Data.Span, DecodeMuLaw);
                case AudioCodec.Pcma:
                    return Decode(frame.Data.Span, DecodeALaw);
                default:
                    throw new NotSupportedException($"{frame.Codec} is not G.711.");
            }
        }

        public static short DecodeMuLaw(byte value)
        {
            value = (byte)~value;
            var exponent = (value >> 4) & 0x07;
            var mantissa = value & 0x0F;
            var sample = (((mantissa << 3) + 0x84) << exponent) - 0x84;
            return (short)((value & 0x80) != 0 ? -sample : sample);
        }

        public static short DecodeALaw(byte value)
        {
            value ^= 0x55;
            var exponent = (value >> 4) & 0x07;
            var mantissa = value & 0x0F;
            var sample = exponent == 0 ? (mantissa << 4) + 8 : ((mantissa << 4) + 0x108) << (exponent - 1);
            return (short)((value & 0x80) != 0 ? sample : -sample);
        }

        #endregion

        #region Private Methods

        private static short[] Decode(ReadOnlySpan<byte> data, Func<byte, short> decode)
        {
            var samples = new short[data.Length];

            for (var i = 0; i < data.Length; i++)
            {
                samples[i] = decode(data[i]);
            }

            return samples;
        }

        #endregion
    }
}
