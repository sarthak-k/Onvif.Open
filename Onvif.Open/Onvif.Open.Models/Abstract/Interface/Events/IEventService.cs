// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Events;

namespace Onvif.Open.Core.Abstract.Interface.Events
{
    /// <summary>
    /// ONVIF Events service (ver10): PullPoint subscriptions.
    /// </summary>
    public interface IEventService
    {
        #region Public Variables

        Uri ServiceUri { get; }

        #endregion

        #region Public Methods

        /// <summary>Creates a PullPoint subscription that expires after <paramref name="terminationTime"/> unless renewed.</summary>
        Task<IPullPointSubscription> CreatePullPointSubscriptionAsync(TimeSpan terminationTime, CancellationToken cancellationToken = default);

        #endregion
    }

    /// <summary>
    /// A PullPoint subscription. Dispose it to unsubscribe.
    /// </summary>
    public interface IPullPointSubscription : IAsyncDisposable
    {
        #region Public Variables

        /// <summary>The subscription manager address returned by the device.</summary>
        Uri Address { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Waits up to <paramref name="timeout"/> (long poll) for at most <paramref name="messageLimit"/> events.
        /// <paramref name="cameraId"/> is copied into the events.
        /// </summary>
        Task<IReadOnlyList<CameraEvent>> PullMessagesAsync(TimeSpan timeout, int messageLimit, string cameraId = null, CancellationToken cancellationToken = default);

        /// <summary>Extends the subscription by <paramref name="terminationTime"/>.</summary>
        Task RenewAsync(TimeSpan terminationTime, CancellationToken cancellationToken = default);

        Task UnsubscribeAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
