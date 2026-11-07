// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Events;
using Onvif.Open.Events.Implementation.Builder;

namespace Onvif.Open.Events.Interface
{
    internal interface IEventServiceBuilder
    {
        #region Public Methods

        IEventService Build();
        EventServiceBuilder SetCamera(ICameraInformation camera);
        EventServiceBuilder SetServiceUri(Uri serviceUri);
        EventServiceBuilder SetTimeProvider(TimeProvider timeProvider);

        #endregion
    }
}
