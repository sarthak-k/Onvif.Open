// Sarthak Killedar
// 2016-03-06 @ 12:39 PM

using System;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Dispatcher;
using System.Security.Cryptography;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Implementation.Security;

namespace Onvif.Open.Core.Implementation
{
    internal class PasswordDigestMessageInspector : IClientMessageInspector
    {
        private readonly ICameraInformation _camera;

        #region Constructor

        public PasswordDigestMessageInspector(ICameraInformation camera)
        {
            _camera = camera;
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
            if (!string.IsNullOrEmpty(_camera.Username))
            {
                // Stamp the token in device time so a skewed device clock does not reject it.
                var created = DeviceClock.For(_camera).UtcNow;
                request.Headers.Add(new UsernameTokenHeader(_camera.Username, _camera.Password, RandomNumberGenerator.GetBytes(16), created));
            }

            return null;
        }

        public void AfterReceiveReply(ref Message reply, object correlationState)
        {
          
        }
         
        #endregion

    }
}