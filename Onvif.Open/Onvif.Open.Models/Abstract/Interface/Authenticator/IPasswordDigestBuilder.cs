// Sarthak Killedar
// 2016-03-06 @ 12:47 PM

using System.ServiceModel.Description;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Implementation;

namespace Onvif.Open.Core.Abstract.Interface.Authenticator
{
    public interface IPasswordDigestBuilder
    {
        #region Constructor

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        IPasswordDigestBuilder SetCamera(ICameraInformation camera);
        PasswordDigestBehaviour Build();

        #endregion

    }
}