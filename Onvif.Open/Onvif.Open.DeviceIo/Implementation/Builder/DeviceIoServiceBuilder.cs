// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.DeviceIo.Implementation.Client;
using Onvif.Open.DeviceIo.Interface;

namespace Onvif.Open.DeviceIo.Implementation.Builder
{
    public sealed class DeviceIoServiceBuilder : IDeviceIoServiceBuilder
    {
        #region Constructor

        private DeviceIoServiceBuilder()
        {

        }

        #endregion

        #region Private Variables

        private ICameraInformation _camera;
        private Uri _serviceUri;

        #endregion

        #region Public Methods

        public IDeviceIoService Build()
        {
            if (_camera == null)
            {
                throw new InvalidOperationException("Camera must be set before building the DeviceIO service.");
            }

            if (_serviceUri == null)
            {
                throw new InvalidOperationException("The DeviceIO service address must be set (see IDeviceManagement.FindServiceUri).");
            }

            return DeviceIoService.CreateInstance(_serviceUri, _camera);
        }

        public DeviceIoServiceBuilder SetCamera(ICameraInformation camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            return this;
        }

        public DeviceIoServiceBuilder SetServiceUri(Uri serviceUri)
        {
            if (serviceUri == null)
            {
                throw new ArgumentNullException(nameof(serviceUri));
            }

            if (!serviceUri.IsAbsoluteUri)
            {
                throw new UriFormatException("DeviceIO service URL must be an absolute URL.");
            }

            _serviceUri = serviceUri;
            return this;
        }

        public static DeviceIoServiceBuilder GetBuilder()
        {
            return new DeviceIoServiceBuilder();
        }

        #endregion
    }
}
