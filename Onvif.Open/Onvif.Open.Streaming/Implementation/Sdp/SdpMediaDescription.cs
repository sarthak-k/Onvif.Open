// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;

namespace Onvif.Open.Streaming.Implementation.Sdp
{
    /// <summary>
    /// One "m=" section of a session description (RFC 4566).
    /// </summary>
    internal sealed class SdpMediaDescription
    {
        #region Constructor

        internal SdpMediaDescription(string mediaType, int payloadType)
        {
            MediaType = mediaType;
            PayloadType = payloadType;
        }

        #endregion

        #region Public Variables

        public string MediaType { get; }
        public int PayloadType { get; }
        public string EncodingName { get; internal set; }
        public int ClockRate { get; internal set; } = 90000;

        /// <summary>Audio channels from "a=rtpmap" (1 when not given).</summary>
        public int Channels { get; internal set; } = 1;
        public string Control { get; internal set; }

        /// <summary>Format parameters from "a=fmtp:" (keys are case-insensitive).</summary>
        public IDictionary<string, string> FormatParameters { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion
    }
}
