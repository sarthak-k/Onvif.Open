// Sarthak Killedar
// 2016-03-06 @ 12:48 PM

using System;
using System.ServiceModel.Description;
using Onvif.Open.Core.Abstract.Interface.Authenticator;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Core.Implementation
{
    public class PasswordDigestBuilder : IPasswordDigestBuilder
    {

        #region Constructor

        private PasswordDigestBuilder()
        {
            
        }

        #endregion

        #region Private Variables

        private ICameraInformation _camera;

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        public IPasswordDigestBuilder SetCamera(ICameraInformation camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            _camera = camera;

            return this;
        }

        public static PasswordDigestBuilder GetBuilder()
        {
            return new PasswordDigestBuilder();
        }

        public PasswordDigestBehaviour Build()
        {
            return PasswordDigestBehaviour.CreateInstance(_camera);
        }

        #endregion
    }
}