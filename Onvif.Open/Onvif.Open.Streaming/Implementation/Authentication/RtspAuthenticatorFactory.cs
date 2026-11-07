// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Onvif.Open.Streaming.Implementation.Authentication
{
    /// <summary>
    /// Picks the strongest authentication scheme offered in the WWW-Authenticate challenges.
    /// </summary>
    internal static class RtspAuthenticatorFactory
    {
        #region Public Methods

        public static IRtspAuthenticator Create(NetworkCredential credential, IEnumerable<string> challenges)
        {
            if (credential == null)
            {
                return null;
            }

            var list = (challenges ?? Enumerable.Empty<string>()).ToList();

            var digest = list.FirstOrDefault(c => c.StartsWith("Digest", StringComparison.OrdinalIgnoreCase));
            if (digest != null)
            {
                return new DigestRtspAuthenticator(credential, ParseParameters(digest.Substring("Digest".Length)));
            }

            if (list.Any(c => c.StartsWith("Basic", StringComparison.OrdinalIgnoreCase)))
            {
                return new BasicRtspAuthenticator(credential);
            }

            return null;
        }

        internal static IDictionary<string, string> ParseParameters(string value)
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;

            while (index < value.Length)
            {
                while (index < value.Length && (value[index] == ',' || char.IsWhiteSpace(value[index])))
                {
                    index++;
                }

                var equals = value.IndexOf('=', index);
                if (equals < 0)
                {
                    break;
                }

                var key = value.Substring(index, equals - index).Trim();
                index = equals + 1;
                string parameterValue;

                if (index < value.Length && value[index] == '"')
                {
                    var end = value.IndexOf('"', index + 1);
                    if (end < 0)
                    {
                        end = value.Length;
                    }

                    parameterValue = value.Substring(index + 1, end - index - 1);
                    index = end + 1;
                }
                else
                {
                    var end = value.IndexOf(',', index);
                    if (end < 0)
                    {
                        end = value.Length;
                    }

                    parameterValue = value.Substring(index, end - index).Trim();
                    index = end;
                }

                parameters[key] = parameterValue;
            }

            return parameters;
        }

        #endregion
    }
}
