// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Events;
using Onvif.Open.Events.Implementation.Client;
using Onvif.Open.Events.Interface;

namespace Onvif.Open.Events.Implementation.Builder
{
    public sealed class EventServiceBuilder : IEventServiceBuilder
    {
        #region Constructor

        private EventServiceBuilder()
        {

        }

        #endregion

        #region Private Variables

        private ICameraInformation _camera;
        private Uri _serviceUri;
        private TimeProvider _timeProvider = TimeProvider.System;

        #endregion

        #region Public Methods

        public IEventService Build()
        {
            if (_camera == null)
            {
                throw new InvalidOperationException("Camera must be set before building the event service.");
            }

            return EventService.CreateInstance(_serviceUri ?? _camera.CameraUri, _camera, _timeProvider);
        }

        public EventServiceBuilder SetCamera(ICameraInformation camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            return this;
        }

        /// <summary>Events service address. Defaults to the camera's device service address when not set.</summary>
        public EventServiceBuilder SetServiceUri(Uri serviceUri)
        {
            if (serviceUri == null)
            {
                throw new ArgumentNullException(nameof(serviceUri));
            }

            if (!serviceUri.IsAbsoluteUri)
            {
                throw new UriFormatException("Event service URL must be an absolute URL.");
            }

            _serviceUri = serviceUri;
            return this;
        }

        public EventServiceBuilder SetTimeProvider(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            return this;
        }

        public static EventServiceBuilder GetBuilder()
        {
            return new EventServiceBuilder();
        }

        #endregion
    }
}
