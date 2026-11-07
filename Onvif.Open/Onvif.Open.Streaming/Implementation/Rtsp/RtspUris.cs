// Sarthak Killedar
// 2026-11-07

using System;
using System.Net;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// RTSP URI handling: control URIs from the session description, credentials embedded in stream URIs.
    /// </summary>
    internal static class RtspUris
    {
        #region Internal Methods

        internal static string ResolveControl(string baseUri, string control)
        {
            if (string.IsNullOrWhiteSpace(control) || control.Trim() == "*")
            {
                return baseUri;
            }

            control = control.Trim();

            if (control.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) || control.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase))
            {
                return control;
            }

            return baseUri.EndsWith("/", StringComparison.Ordinal) ? baseUri + control : baseUri + "/" + control;
        }

        internal static string WithoutUserInfo(Uri uri)
        {
            if (string.IsNullOrEmpty(uri.UserInfo))
            {
                return uri.OriginalString;
            }

            return new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
        }

        internal static NetworkCredential CredentialFromUri(Uri uri)
        {
            if (string.IsNullOrEmpty(uri.UserInfo))
            {
                return null;
            }

            var parts = uri.UserInfo.Split(new[] { ':' }, 2);
            return new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }

        #endregion
    }
}
