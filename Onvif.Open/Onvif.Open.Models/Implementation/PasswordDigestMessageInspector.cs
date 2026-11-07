// Sarthak Killedar
// 2016-03-06 @ 12:39 PM

using System;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using Onvif.Open.Core.Implementation.Security;

namespace Onvif.Open.Core.Implementation
{
    internal class PasswordDigestMessageInspector : IClientMessageInspector
    {
        private readonly string _username;
        private readonly string _password;

        #region Constructor

        public PasswordDigestMessageInspector(string username, string password)
        {
            _username = username;
            _password = password;
        }

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        public object BeforeSendRequest(ref Message request, IClientChannel channel)
        {
            // Anonymous access: devices allow some calls (e.g. GetSystemDateAndTime) without credentials.
            if (!string.IsNullOrEmpty(_username))
            {
                request.Headers.Add(new UsernameTokenHeader(_username, _password));
            }

            return null;
        }

        public void AfterReceiveReply(ref Message reply, object correlationState)
        {
          
        }
         
        #endregion

    }
}