// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Ptz.Implementation.Client;
using Onvif.Open.Ptz.Interface;

namespace Onvif.Open.Ptz.Implementation.Builder
{
    public sealed class PtzServiceBuilder : IPtzServiceBuilder
    {
        #region Constructor

        private PtzServiceBuilder()
        {

        }

        #endregion

        #region Private Variables

        private ICameraInformation _camera;
        private Uri _serviceUri;

        #endregion

        #region Public Methods

        public IPtzService Build()
        {
            if (_camera == null)
            {
                throw new InvalidOperationException("Camera must be set before building the PTZ service.");
            }

            return PtzService.CreateInstance(_serviceUri ?? _camera.CameraUri, _camera);
        }

        public PtzServiceBuilder SetCamera(ICameraInformation camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            return this;
        }

        public PtzServiceBuilder SetServiceUri(Uri serviceUri)
        {
            if (serviceUri == null)
            {
                throw new ArgumentNullException(nameof(serviceUri));
            }

            if (!serviceUri.IsAbsoluteUri)
            {
                throw new UriFormatException("PTZ service URL must be an absolute URL.");
            }

            _serviceUri = serviceUri;
            return this;
        }

        public static PtzServiceBuilder GetBuilder()
        {
            return new PtzServiceBuilder();
        }

        #endregion
    }
}
