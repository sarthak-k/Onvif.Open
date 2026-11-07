// Sarthak Killedar
// 2026-11-07

using System;
using System.Globalization;

namespace Onvif.Open.Streaming.Implementation.Transport
{
    /// <summary>
    /// Helpers for the parameters of the RTSP "Transport" header (RFC 2326 §12.39).
    /// </summary>
    internal static class TransportHeader
    {
        #region Public Methods

        public static string GetParameter(string transport, string name)
        {
            foreach (var part in (transport ?? string.Empty).Split(';'))
            {
                var separator = part.IndexOf('=');
                if (separator > 0 && string.Equals(part.Substring(0, separator).Trim(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return part.Substring(separator + 1).Trim();
                }
            }

            return null;
        }

        /// <summary>Parses "a-b" (or "a") into its first and second number.</summary>
        public static bool TryGetRange(string transport, string name, out int first, out int second)
        {
            first = second = -1;
            var value = GetParameter(transport, name);

            if (value == null)
            {
                return false;
            }

            var numbers = value.Split('-');
            if (!int.TryParse(numbers[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out first))
            {
                return false;
            }

            second = numbers.Length > 1 && int.TryParse(numbers[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : first + 1;

            return true;
        }

        #endregion
    }
}
