// Sarthak Killedar
// 2026-11-07

namespace Onvif.Open.Streaming.Implementation.Authentication
{
    /// <summary>
    /// Strategy producing the RTSP "Authorization" header for a request.
    /// </summary>
    internal interface IRtspAuthenticator
    {
        #region Public Methods

        string GetAuthorization(string method, string uri);

        #endregion
    }
}
