// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Recording.Implementation.Format
{
    /// <summary>
    /// ADTS header (ISO/IEC 13818-7) that frames raw AAC access units for MPEG-TS, built from the stream's
    /// AudioSpecificConfig (or, without one, from the sample rate and channels, assuming AAC-LC).
    /// </summary>
    internal sealed class AdtsHeader
    {
        #region Constructor

        private AdtsHeader(int profile, int frequencyIndex, int channelConfiguration)
        {
            _profile = profile;
            _frequencyIndex = frequencyIndex;
            _channelConfiguration = channelConfiguration;
        }

        #endregion

        #region Private Variables

        public const int Size = 7;
        private const int MaxFrameLength = 0x1FFF;

        private static readonly int[] SampleRates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };

        private readonly int _profile;
        private readonly int _frequencyIndex;
        private readonly int _channelConfiguration;

        #endregion

        #region Public Methods

        public static bool TryCreate(AudioFormat format, out AdtsHeader header)
        {
            header = null;
            var config = format.Config.Span;
            int objectType, frequencyIndex, channels;

            if (config.Length >= 2)
            {
                objectType = config[0] >> 3;
                frequencyIndex = ((config[0] & 0x07) << 1) | (config[1] >> 7);
                channels = (config[1] >> 3) & 0x0F;
            }
            else
            {
                objectType = 2;
                frequencyIndex = Array.IndexOf(SampleRates, format.SampleRate);
                channels = format.Channels;
            }

            // ADTS can only express object types 1-4 (Main, LC, SSR, LTP), the 13 standard rates and 1-7 channels.
            if (objectType < 1 || objectType > 4 || frequencyIndex < 0 || frequencyIndex >= SampleRates.Length || channels < 1 || channels > 7)
            {
                return false;
            }

            header = new AdtsHeader(objectType - 1, frequencyIndex, channels);
            return true;
        }

        /// <summary>Writes the header for an access unit of <paramref name="accessUnitLength"/> bytes.</summary>
        public bool TryWrite(Span<byte> destination, int accessUnitLength)
        {
            var frameLength = Size + accessUnitLength;

            if (destination.Length < Size || frameLength > MaxFrameLength)
            {
                return false;
            }

            destination[0] = 0xFF;
            destination[1] = 0xF1;                                                       // MPEG-4, layer 0, no CRC
            destination[2] = (byte)((_profile << 6) | (_frequencyIndex << 2) | (_channelConfiguration >> 2));
            destination[3] = (byte)(((_channelConfiguration & 0x03) << 6) | (frameLength >> 11));
            destination[4] = (byte)(frameLength >> 3);
            destination[5] = (byte)(((frameLength & 0x07) << 5) | 0x1F);                 // buffer fullness 0x7FF (VBR)
            destination[6] = 0xFC;                                                       // ... and one raw data block
            return true;
        }

        #endregion
    }
}
