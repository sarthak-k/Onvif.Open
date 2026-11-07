// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Replay;
using Onvif.Open.Replay.Implementation.Client;
using Onvif.Open.Replay.Interface;

namespace Onvif.Open.Replay.Implementation.Builder
{
    /// <summary>
    /// Builds the Recording Search service client. The service address comes from
    /// IDeviceManagement.FindServiceUri(OnvifServiceNamespaces.Search).
    /// </summary>
    public sealed class RecordingSearchServiceBuilder : IRecordingSearchServiceBuilder
    {
        #region Constructor

        private RecordingSearchServiceBuilder()
        {

        }

        #endregion

        #region Private Variables

        private ICameraInformation _camera;
        private Uri _serviceUri;

        #endregion

        #region Public Methods

        public IRecordingSearchService Build()
        {
            ServiceAddress.Require(_camera, _serviceUri, "Recording Search");
            return RecordingSearchService.CreateInstance(_serviceUri, _camera);
        }

        public RecordingSearchServiceBuilder SetCamera(ICameraInformation camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            return this;
        }

        public RecordingSearchServiceBuilder SetServiceUri(Uri serviceUri)
        {
            _serviceUri = ServiceAddress.Validate(serviceUri, "Recording Search");
            return this;
        }

        public static RecordingSearchServiceBuilder GetBuilder()
        {
            return new RecordingSearchServiceBuilder();
        }

        #endregion
    }

    /// <summary>
    /// Builds the Replay service client. The service address comes from
    /// IDeviceManagement.FindServiceUri(OnvifServiceNamespaces.Replay).
    /// </summary>
    public sealed class ReplayServiceBuilder : IReplayServiceBuilder
    {
        #region Constructor

        private ReplayServiceBuilder()
        {

        }

        #endregion

        #region Private Variables

        private ICameraInformation _camera;
        private Uri _serviceUri;

        #endregion

        #region Public Methods

        public IReplayService Build()
        {
            ServiceAddress.Require(_camera, _serviceUri, "Replay");
            return ReplayService.CreateInstance(_serviceUri, _camera);
        }

        public ReplayServiceBuilder SetCamera(ICameraInformation camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            return this;
        }

        public ReplayServiceBuilder SetServiceUri(Uri serviceUri)
        {
            _serviceUri = ServiceAddress.Validate(serviceUri, "Replay");
            return this;
        }

        public static ReplayServiceBuilder GetBuilder()
        {
            return new ReplayServiceBuilder();
        }

        #endregion
    }

    internal static class ServiceAddress
    {
        public static Uri Validate(Uri serviceUri, string service)
        {
            if (serviceUri == null)
            {
                throw new ArgumentNullException(nameof(serviceUri));
            }

            if (!serviceUri.IsAbsoluteUri)
            {
                throw new UriFormatException($"{service} service URL must be an absolute URL.");
            }

            return serviceUri;
        }

        public static void Require(ICameraInformation camera, Uri serviceUri, string service)
        {
            if (camera == null)
            {
                throw new InvalidOperationException($"Camera must be set before building the {service} service.");
            }

            if (serviceUri == null)
            {
                throw new InvalidOperationException($"The {service} service address must be set (see IDeviceManagement.FindServiceUri).");
            }
        }
    }
}
