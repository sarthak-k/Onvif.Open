// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Streaming.Implementation.Metadata;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Sdp;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// Reads the ONVIF metadata track (application/vnd.onvif.metadata): the XML document of one RTP timestamp is
    /// split over packets, the marker bit ends it.
    /// </summary>
    internal sealed class MetadataTrackReader : IRtpTrackReader<AnalyticsFrame>
    {
        #region Private Variables

        public const string EncodingName = "vnd.onvif.metadata";

        /// <summary>Larger documents are dropped rather than buffered without bound.</summary>
        private const int MaxDocumentBytes = 1024 * 1024;

        private readonly MemoryStream _document = new MemoryStream();
        private uint? _timestamp;
        private bool _overflow;

        #endregion

        #region Public Methods

        public IReadOnlyList<SdpMediaDescription> SelectTracks(SessionDescription sessionDescription)
        {
            return new[]
            {
                sessionDescription.Media.FirstOrDefault(m => string.Equals(m.EncodingName, EncodingName, StringComparison.OrdinalIgnoreCase))
                ?? throw new NotSupportedException("The RTSP stream does not contain an ONVIF metadata track: add a metadata configuration to the media profile on the camera.")
            };
        }

        public void Process(int track, RtpPacket packet, ICollection<AnalyticsFrame> frames)
        {
            if (_timestamp.HasValue && _timestamp.Value != packet.Timestamp)
            {
                // A new document started before the previous one was terminated: the end was lost.
                Reset();
            }

            _timestamp = packet.Timestamp;

            if (!_overflow)
            {
                if (_document.Length + packet.Payload.Length > MaxDocumentBytes)
                {
                    _overflow = true;
                    _document.SetLength(0);
                }
                else
                {
                    _document.Write(packet.Payload.Span);
                }
            }

            if (!packet.Marker)
            {
                return;
            }

            if (!_overflow)
            {
                var text = Encoding.UTF8.GetString(_document.GetBuffer(), 0, (int)_document.Length);

                foreach (var frame in AnalyticsMetadataParser.Parse(text))
                {
                    frames.Add(frame);
                }
            }

            Reset();
        }

        public DateTimeOffset? GetCaptureTime(AnalyticsFrame frame)
        {
            return frame.UtcTime;
        }

        #endregion

        #region Private Methods

        private void Reset()
        {
            _document.SetLength(0);
            _timestamp = null;
            _overflow = false;
        }

        #endregion
    }
}
