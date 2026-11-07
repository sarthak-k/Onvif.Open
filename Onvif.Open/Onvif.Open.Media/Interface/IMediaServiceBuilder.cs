// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Media.Implementation.Builder;

namespace Onvif.Open.Media.Interface
{
    internal interface IMediaServiceBuilder
    {
        #region Public Methods

        IMediaService Build();
        MediaServiceBuilder SetCamera(ICameraInformation camera);
        MediaServiceBuilder SetServiceUri(Uri serviceUri);

        #endregion
    }
}
