// Sarthak Killedar
// 2026-11-07

using System;
using System.Net;
using System.Text;

namespace Onvif.Open.Streaming.Implementation.Authentication
{
    internal sealed class BasicRtspAuthenticator : IRtspAuthenticator
    {
        #region Constructor

        public BasicRtspAuthenticator(NetworkCredential credential)
        {
            _value = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(credential.UserName + ":" + credential.Password));
        }

        #endregion

        #region Private Variables

        private readonly string _value;

        #endregion

        #region Public Methods

        public string GetAuthorization(string method, string uri)
        {
            return _value;
        }

        #endregion
    }
}
