// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Onvif.Open.Streaming.Implementation.Sdp
{
    /// <summary>
    /// Minimal SDP (RFC 4566) parser: extracts what is needed to SETUP and decode RTP media.
    /// </summary>
    internal sealed class SessionDescription
    {
        #region Constructor

        private SessionDescription(string raw)
        {
            Raw = raw;
        }

        #endregion

        #region Public Variables

        public string Raw { get; }
        public string Control { get; private set; }
        public IReadOnlyList<SdpMediaDescription> Media => _media;

        public SdpMediaDescription Video => _media.FirstOrDefault(m => string.Equals(m.MediaType, "video", StringComparison.OrdinalIgnoreCase));
        public SdpMediaDescription Audio => _media.FirstOrDefault(m => string.Equals(m.MediaType, "audio", StringComparison.OrdinalIgnoreCase));

        #endregion

        #region Private Variables

        private readonly List<SdpMediaDescription> _media = new List<SdpMediaDescription>();

        #endregion

        #region Public Methods

        public static SessionDescription Parse(string sdp)
        {
            var description = new SessionDescription(sdp ?? string.Empty);
            SdpMediaDescription current = null;

            using (var reader = new StringReader(description.Raw))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();

                    if (line.Length < 2 || line[1] != '=')
                    {
                        continue;
                    }

                    var value = line.Substring(2);

                    switch (line[0])
                    {
                        case 'm':
                            current = ParseMediaLine(value);
                            if (current != null)
                            {
                                description._media.Add(current);
                            }
                            break;
                        case 'a':
                            ParseAttribute(description, current, value);
                            break;
                    }
                }
            }

            return description;
        }

        #endregion

        #region Private Methods

        private static SdpMediaDescription ParseMediaLine(string value)
        {
            // m=<media> <port> <proto> <fmt> ...
            var parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 4)
            {
                return null;
            }

            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var payloadType);
            return new SdpMediaDescription(parts[0], payloadType);
        }

        private static void ParseAttribute(SessionDescription description, SdpMediaDescription media, string value)
        {
            var separator = value.IndexOf(':');
            var name = separator < 0 ? value : value.Substring(0, separator);
            var attributeValue = separator < 0 ? string.Empty : value.Substring(separator + 1).Trim();

            if (string.Equals(name, "control", StringComparison.OrdinalIgnoreCase))
            {
                if (media == null)
                {
                    description.Control = attributeValue;
                }
                else
                {
                    media.Control = attributeValue;
                }

                return;
            }

            if (media == null)
            {
                return;
            }

            if (string.Equals(name, "rtpmap", StringComparison.OrdinalIgnoreCase))
            {
                // a=rtpmap:<payload type> <encoding name>/<clock rate>[/<encoding parameters>]
                var parts = attributeValue.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && IsForPayload(parts[0], media))
                {
                    var encoding = parts[1].Split('/');
                    media.EncodingName = encoding[0].Trim();
                    if (encoding.Length > 1 && int.TryParse(encoding[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var clockRate) && clockRate > 0)
                    {
                        media.ClockRate = clockRate;
                    }

                    if (encoding.Length > 2 && int.TryParse(encoding[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channels) && channels > 0)
                    {
                        media.Channels = channels;
                    }
                }
            }
            else if (string.Equals(name, "fmtp", StringComparison.OrdinalIgnoreCase))
            {
                // a=fmtp:<payload type> key=value;key=value
                var parts = attributeValue.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && IsForPayload(parts[0], media))
                {
                    foreach (var parameter in parts[1].Split(';'))
                    {
                        var equals = parameter.IndexOf('=');
                        if (equals > 0)
                        {
                            media.FormatParameters[parameter.Substring(0, equals).Trim()] = parameter.Substring(equals + 1).Trim();
                        }
                    }
                }
            }
        }

        private static bool IsForPayload(string payloadType, SdpMediaDescription media)
        {
            return int.TryParse(payloadType, NumberStyles.Integer, CultureInfo.InvariantCulture, out var type) && type == media.PayloadType;
        }

        #endregion
    }
}
