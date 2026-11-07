// Sarthak Killedar
// 2026-11-07

using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Camera.Interface
{
    internal interface ICameraMediaBuilder
    {
        #region Public Methods

        ICameraMedia Build();
        CameraMediaBuilder SetCamera(ICameraInformation camera);
        CameraMediaBuilder SetDeviceManagement(ICameraDeviceManagement cameraDeviceManagement);

        #endregion
    }
}
