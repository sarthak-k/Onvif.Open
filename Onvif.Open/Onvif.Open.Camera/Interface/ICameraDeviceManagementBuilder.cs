// Sarthak Killedar
// 2016-03-06 @ 9:16 AM

using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Device.Management.Implementation;

namespace Onvif.Open.Camera.Interface
{
    internal interface ICameraDeviceManagementBuilder
    {
        ICameraDeviceManagement Build();
        CameraDeviceManagementBuilder SetDeviceManagement(ICameraInformation camera);
    }
}