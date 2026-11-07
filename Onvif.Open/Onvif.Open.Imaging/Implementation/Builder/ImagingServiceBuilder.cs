// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Imaging.Implementation.Client;
using Onvif.Open.Imaging.Interface;

namespace Onvif.Open.Imaging.Implementation.Builder
{
    public sealed class ImagingServiceBuilder : IImagingServiceBuilder
    {
        #region Constructor

        private ImagingServiceBuilder()
        {

        }

        #endregion

        #region Private Variables

        private ICameraInformation _camera;
        private Uri _serviceUri;

        #endregion

        #region Public Methods

        public IImagingService Build()
        {
            if (_camera == null)
            {
                throw new InvalidOperationException("Camera must be set before building the imaging service.");
            }

            return ImagingService.CreateInstance(_serviceUri ?? _camera.CameraUri, _camera);
        }

        public ImagingServiceBuilder SetCamera(ICameraInformation camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            return this;
        }

        public ImagingServiceBuilder SetServiceUri(Uri serviceUri)
        {
            if (serviceUri == null)
            {
                throw new ArgumentNullException(nameof(serviceUri));
            }

            if (!serviceUri.IsAbsoluteUri)
            {
                throw new UriFormatException("Imaging service URL must be an absolute URL.");
            }

            _serviceUri = serviceUri;
            return this;
        }

        public static ImagingServiceBuilder GetBuilder()
        {
            return new ImagingServiceBuilder();
        }

        #endregion
    }
}
