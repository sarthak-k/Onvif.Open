// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// An RTSP message received from the device (normally a response; server requests are surfaced with <see cref="IsRequest"/>).
    /// </summary>
    internal sealed class RtspResponse
    {
        #region Constructor

        internal RtspResponse(string startLine, IReadOnlyList<KeyValuePair<string, string>> headers, string body)
        {
            StartLine = startLine;
            Headers = headers;
            Body = body ?? string.Empty;

            IsRequest = !startLine.StartsWith("RTSP/", StringComparison.OrdinalIgnoreCase);

            if (!IsRequest)
            {
                var parts = startLine.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var status))
                {
                    StatusCode = status;
                }

                Reason = parts.Length == 3 ? parts[2] : string.Empty;
            }
        }

        #endregion

        #region Public Variables

        public string StartLine { get; }
        public bool IsRequest { get; }
        public int StatusCode { get; }
        public string Reason { get; }
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }
        public string Body { get; }

        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

        public int? CSeq => int.TryParse(GetHeader("CSeq"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cseq) ? cseq : (int?)null;

        #endregion

        #region Public Methods

        public string GetHeader(string name)
        {
            return GetHeaders(name).FirstOrDefault();
        }

        public IEnumerable<string> GetHeaders(string name)
        {
            return Headers.Where(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value);
        }

        #endregion
    }
}
