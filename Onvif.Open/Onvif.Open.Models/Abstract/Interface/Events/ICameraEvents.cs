// Sarthak Killedar
// 2026-11-07

using System.Collections.Generic;
using System.Threading;
using Onvif.Open.Core.Events;

namespace Onvif.Open.Core.Abstract.Interface.Events
{
    /// <summary>
    /// The device's ONVIF Events service.
    /// </summary>
    public interface ICameraEvents
    {
        #region Public Methods

        /// <summary>
        /// Subscribes (PullPoint) and yields the device's events until cancelled. The subscription is renewed
        /// automatically, re-created after errors, and removed when enumeration stops.
        /// </summary>
        IAsyncEnumerable<CameraEvent> ReadEventsAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
