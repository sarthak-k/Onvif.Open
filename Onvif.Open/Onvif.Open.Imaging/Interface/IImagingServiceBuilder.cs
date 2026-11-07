// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Imaging.Implementation.Builder;

namespace Onvif.Open.Imaging.Interface
{
    internal interface IImagingServiceBuilder
    {
        #region Public Methods

        IImagingService Build();
        ImagingServiceBuilder SetCamera(ICameraInformation camera);
        ImagingServiceBuilder SetServiceUri(Uri serviceUri);

        #endregion
    }
}
