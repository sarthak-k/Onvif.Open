// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Camera.Interface;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Camera.Implementation
{
    public class CameraMediaBuilder : ICameraMediaBuilder
    {
        #region Constructor

        private CameraMediaBuilder()
        {

        }

        #endregion

        #region Private Variables

        private ICameraInformation Camera { get; set; }
        private ICameraDeviceManagement CameraDeviceManagement { get; set; }

        #endregion

        #region Public Methods

        public ICameraMedia Build()
        {
            if (Camera == null || CameraDeviceManagement == null)
            {
                throw new InvalidOperationException("Camera and Device Management must be set before building Camera Media.");
            }

            return CameraMedia.CreateInstance(Camera, CameraDeviceManagement.GetDeviceManagement());
        }

        public CameraMediaBuilder SetCamera(ICameraInformation camera)
        {
            Camera = camera ?? throw new ArgumentNullException(nameof(camera));
            return this;
        }

        public CameraMediaBuilder SetDeviceManagement(ICameraDeviceManagement cameraDeviceManagement)
        {
            CameraDeviceManagement = cameraDeviceManagement ?? throw new ArgumentNullException(nameof(cameraDeviceManagement));
            return this;
        }

        internal static CameraMediaBuilder GetBuilder()
        {
            return new CameraMediaBuilder();
        }

        #endregion
    }
}
