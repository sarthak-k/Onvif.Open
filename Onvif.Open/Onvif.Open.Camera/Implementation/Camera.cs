// Sarthak Killedar
// 2016-03-03 @ 9:57 PM

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Camera.Implementation
{
    public class Camera : ICamera, ICameraInformation
    {

        #region Constructor

        private Camera(Uri cameraUri, string username, string password)
        {
            CameraUri = cameraUri;
            Username = username;
            Password = password;
        }

        #endregion

        #region Private Variables

        private ICameraDeviceManagement CameraDeviceManagement { get; set; }
        
        #endregion

        #region Public Variables


        public Uri CameraUri { get; }
        public string Username { get; }
        public string Password { get; }

        public ICameraDeviceManagement Initialize()
        {
            return CameraDeviceManagement ??
                   (CameraDeviceManagement =
                       CameraDeviceManagementBuilder.GetBuilder().SetDeviceManagement(this).Build());
        }

        public ICameraInformation GetCameraInformation()
        {
            return this;
        }

        #endregion

        #region Private Methods

        internal static ICamera CreateInstance(Uri cameraUri, string username, string password)
        {
            return new Camera(cameraUri, username, password);
        }


        #endregion

        #region Public Methods

        #endregion

    }
}