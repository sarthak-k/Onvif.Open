// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Streaming.Implementation.Sdp;

namespace Onvif.Open.Streaming.Implementation.Depacketizer
{
    /// <summary>
    /// Factory selecting the depacketizer strategy for the encoding announced in the SDP.
    /// </summary>
    internal static class RtpDepacketizerFactory
    {
        #region Public Methods

        public static IRtpDepacketizer Create(SdpMediaDescription media)
        {
            if (media == null)
            {
                throw new ArgumentNullException(nameof(media));
            }

            switch ((media.EncodingName ?? string.Empty).ToUpperInvariant())
            {
                case "H264":
                    return new H264Depacketizer(media.ClockRate, Decode(media, "sprop-parameter-sets"));
                case "H265":
                case "HEVC":
                    return new H265Depacketizer(media.ClockRate,
                        Decode(media, "sprop-vps").Concat(Decode(media, "sprop-sps")).Concat(Decode(media, "sprop-pps")));
                default:
                    return new GenericDepacketizer(media.ClockRate);
            }
        }

        #endregion

        #region Private Methods

        private static IEnumerable<byte[]> Decode(SdpMediaDescription media, string parameter)
        {
            if (!media.FormatParameters.TryGetValue(parameter, out var value) || string.IsNullOrWhiteSpace(value))
            {
                yield break;
            }

            foreach (var item in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                byte[] decoded;
                try
                {
                    decoded = Convert.FromBase64String(item.Trim());
                }
                catch (FormatException)
                {
                    continue;
                }

                yield return decoded;
            }
        }

        #endregion
    }
}
