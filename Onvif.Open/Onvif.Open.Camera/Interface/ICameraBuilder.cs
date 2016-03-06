// Sarthak Killedar
// 2016-03-03 @ 10:09 PM

using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Camera.Interface
{
    internal interface ICameraBuilder
    {
        #region Constructor

        #endregion

        #region Private Variables

        #endregion

        #region Public Variables

        ICamera Build();
        CameraBuilder SetUrl(string url);
        CameraBuilder SetUsername(string username);
        CameraBuilder SetPassword(string password);

        #endregion

        #region Private Methods

        #endregion

        #region Public Methods

        #endregion

    }
}