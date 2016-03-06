// Sarthak Killedar
// 2016-03-06 @ 12:39 PM

using System;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using System.Xml;
using Microsoft.Web.Services3.Security.Tokens;

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
            // Use the WSE 3.0 security token class
            UsernameToken token = new UsernameToken(_username, _password, PasswordOption.SendHashed);

            // Serialize the token to XML
            XmlElement securityToken = token.GetXml(new XmlDocument());

            //
            MessageHeader securityHeader = MessageHeader.CreateHeader("Security", "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd", securityToken, false);
            request.Headers.Add(securityHeader);

            // complete
            return Convert.DBNull;
        }

        public void AfterReceiveReply(ref Message reply, object correlationState)
        {
          
        }
         
        #endregion

    }
}