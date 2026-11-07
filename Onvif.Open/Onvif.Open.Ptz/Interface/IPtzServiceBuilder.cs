// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Ptz.Implementation.Builder;

namespace Onvif.Open.Ptz.Interface
{
    internal interface IPtzServiceBuilder
    {
        #region Public Methods

        IPtzService Build();
        PtzServiceBuilder SetCamera(ICameraInformation camera);
        PtzServiceBuilder SetServiceUri(Uri serviceUri);

        #endregion
    }
}
