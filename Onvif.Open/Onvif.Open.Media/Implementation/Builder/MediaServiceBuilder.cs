// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Core.Operations;
using Onvif.Open.Media.Implementation.Client;
using Onvif.Open.Media.Interface;

namespace Onvif.Open.Media.Implementation.Builder
{
    public sealed class MediaServiceBuilder : IMediaServiceBuilder
    {
        #region Constructor

        private MediaServiceBuilder()
        {

        }

        #endregion

        #region Private Variables

        private ICameraInformation _camera;
        private Uri _serviceUri;
        private MediaServiceVersion _version = MediaServiceVersion.Media1;

        #endregion

        #region Public Methods

        public IMediaService Build()
        {
            if (_camera == null)
            {
                throw new InvalidOperationException("Camera must be set before building the media service.");
            }

            var serviceUri = _serviceUri ?? _camera.CameraUri;
            return _version == MediaServiceVersion.Media2
                ? Media2Service.CreateInstance(serviceUri, _camera)
                : MediaService.CreateInstance(serviceUri, _camera);
        }

        public MediaServiceBuilder SetCamera(ICameraInformation camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            return this;
        }

        /// <summary>
        /// Media service address. Defaults to the camera's device service address when not set.
        /// </summary>
        public MediaServiceBuilder SetServiceUri(Uri serviceUri)
        {
            if (serviceUri == null)
            {
                throw new ArgumentNullException(nameof(serviceUri));
            }

            if (!serviceUri.IsAbsoluteUri)
            {
                throw new UriFormatException("Media service URL must be an absolute URL.");
            }

            _serviceUri = serviceUri;
            return this;
        }

        /// <summary>Media1 (default) or Media2; the service address must belong to that service.</summary>
        public MediaServiceBuilder SetVersion(MediaServiceVersion version)
        {
            if (version == MediaServiceVersion.Auto)
            {
                throw new ArgumentException("Choose Media1 or Media2: the builder does not query the device.", nameof(version));
            }

            _version = version;
            return this;
        }

        public static MediaServiceBuilder GetBuilder()
        {
            return new MediaServiceBuilder();
        }

        #endregion
    }
}
