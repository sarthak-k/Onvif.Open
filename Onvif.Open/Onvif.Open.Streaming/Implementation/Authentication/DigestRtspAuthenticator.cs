// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Onvif.Open.Streaming.Implementation.Authentication
{
    /// <summary>
    /// HTTP Digest (RFC 2617) as used by RTSP (RFC 2326), MD5 with optional qop=auth.
    /// </summary>
    internal sealed class DigestRtspAuthenticator : IRtspAuthenticator
    {
        #region Constructor

        public DigestRtspAuthenticator(NetworkCredential credential, IDictionary<string, string> challenge, string clientNonce = null)
        {
            _credential = credential;
            challenge.TryGetValue("realm", out _realm);
            challenge.TryGetValue("nonce", out _nonce);
            challenge.TryGetValue("opaque", out _opaque);
            challenge.TryGetValue("algorithm", out _algorithm);

            if (challenge.TryGetValue("qop", out var qop))
            {
                foreach (var option in qop.Split(','))
                {
                    if (string.Equals(option.Trim(), "auth", StringComparison.OrdinalIgnoreCase))
                    {
                        _qop = "auth";
                    }
                }
            }

            _clientNonce = clientNonce ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        }

        #endregion

        #region Private Variables

        private readonly NetworkCredential _credential;
        private readonly string _realm;
        private readonly string _nonce;
        private readonly string _opaque;
        private readonly string _algorithm;
        private readonly string _qop;
        private readonly string _clientNonce;
        private int _nonceCount;

        #endregion

        #region Public Methods

        public string GetAuthorization(string method, string uri)
        {
            var ha1 = Md5($"{_credential.UserName}:{_realm}:{_credential.Password}");
            var ha2 = Md5($"{method}:{uri}");

            var builder = new StringBuilder();
            builder.Append("Digest username=\"").Append(_credential.UserName)
                .Append("\", realm=\"").Append(_realm)
                .Append("\", nonce=\"").Append(_nonce)
                .Append("\", uri=\"").Append(uri).Append('"');

            string response;

            if (_qop != null)
            {
                var nonceCount = (++_nonceCount).ToString("x8", CultureInfo.InvariantCulture);
                response = Md5($"{ha1}:{_nonce}:{nonceCount}:{_clientNonce}:{_qop}:{ha2}");
                builder.Append(", qop=").Append(_qop)
                    .Append(", nc=").Append(nonceCount)
                    .Append(", cnonce=\"").Append(_clientNonce).Append('"');
            }
            else
            {
                response = Md5($"{ha1}:{_nonce}:{ha2}");
            }

            builder.Append(", response=\"").Append(response).Append('"');

            if (!string.IsNullOrEmpty(_algorithm))
            {
                builder.Append(", algorithm=").Append(_algorithm);
            }

            if (!string.IsNullOrEmpty(_opaque))
            {
                builder.Append(", opaque=\"").Append(_opaque).Append('"');
            }

            return builder.ToString();
        }

        #endregion

        #region Private Methods

        private static string Md5(string value)
        {
            return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }

        #endregion
    }
}
