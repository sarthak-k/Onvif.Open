// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.DeviceIo.Implementation.Builder;

namespace Onvif.Open.DeviceIo.Interface
{
    internal interface IDeviceIoServiceBuilder
    {
        #region Public Methods

        IDeviceIoService Build();
        DeviceIoServiceBuilder SetCamera(ICameraInformation camera);
        DeviceIoServiceBuilder SetServiceUri(Uri serviceUri);

        #endregion
    }
}
