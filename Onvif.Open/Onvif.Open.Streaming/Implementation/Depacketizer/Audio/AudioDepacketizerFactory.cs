// Sarthak Killedar
// 2026-11-07

using System;
using System.Globalization;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Sdp;

namespace Onvif.Open.Streaming.Implementation.Depacketizer.Audio
{
    /// <summary>
    /// Chooses the audio depacketizer for the track's encoding; null when the codec is not supported
    /// (the stream then plays without audio).
    /// </summary>
    internal static class AudioDepacketizerFactory
    {
        #region Private Variables

        private const int PcmuPayloadType = 0;
        private const int PcmaPayloadType = 8;

        #endregion

        #region Public Methods

        public static IAudioDepacketizer Create(SdpMediaDescription media)
        {
            if (media == null)
            {
                return null;
            }

            var encoding = (media.EncodingName ?? string.Empty).ToUpperInvariant();

            // Static payload types may come without "a=rtpmap".
            if (encoding == "PCMU" || (encoding.Length == 0 && media.PayloadType == PcmuPayloadType))
            {
                return new G711Depacketizer(AudioCodec.Pcmu, G711Rate(media), media.Channels);
            }

            if (encoding == "PCMA" || (encoding.Length == 0 && media.PayloadType == PcmaPayloadType))
            {
                return new G711Depacketizer(AudioCodec.Pcma, G711Rate(media), media.Channels);
            }

            if (encoding == "MPEG4-GENERIC" && IsAac(media))
            {
                var config = Hex(Parameter(media, "config"));
                var format = new AudioFormat(AudioCodec.Aac, media.ClockRate, ChannelsFromConfig(config) ?? media.Channels, config);
                var mode = Parameter(media, "mode") ?? string.Empty;
                var lowBitrate = mode.Equals("AAC-lbr", StringComparison.OrdinalIgnoreCase);

                return new AacDepacketizer(
                    format,
                    Number(media, "sizelength") ?? (lowBitrate ? 6 : 13),
                    Number(media, "indexlength") ?? (lowBitrate ? 2 : 3),
                    Number(media, "indexdeltalength") ?? (lowBitrate ? 2 : 3));
            }

            return null;
        }

        #endregion

        #region Private Methods

        private static bool IsAac(SdpMediaDescription media)
        {
            var mode = Parameter(media, "mode");
            var streamType = Parameter(media, "streamtype");
            return (mode == null || mode.StartsWith("AAC", StringComparison.OrdinalIgnoreCase)) && (streamType == null || streamType == "5");
        }

        private static int G711Rate(SdpMediaDescription media)
        {
            // The 90 kHz default of video does not apply; G.711 is 8 kHz unless the SDP says otherwise.
            return media.EncodingName == null || media.ClockRate == 90000 ? 8000 : media.ClockRate;
        }

        private static int? ChannelsFromConfig(byte[] config)
        {
            // AudioSpecificConfig: audioObjectType (5 bits), samplingFrequencyIndex (4 bits), channelConfiguration (4 bits).
            if (config.Length < 2)
            {
                return null;
            }

            var channels = (config[1] >> 3) & 0x0F;
            return channels > 0 ? channels : (int?)null;
        }

        private static string Parameter(SdpMediaDescription media, string name)
        {
            return media.FormatParameters.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
        }

        private static int? Number(SdpMediaDescription media, string name)
        {
            return int.TryParse(Parameter(media, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (int?)null;
        }

        private static byte[] Hex(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length % 2 != 0)
            {
                return Array.Empty<byte>();
            }

            try
            {
                return Convert.FromHexString(value);
            }
            catch (FormatException)
            {
                return Array.Empty<byte>();
            }
        }

        #endregion
    }
}
