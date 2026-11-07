// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Abstract.Interface.Events;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Events.Implementation.Builder;

namespace Onvif.Open.Camera.Implementation
{
    /// <summary>
    /// Reads a camera's events through a PullPoint subscription that is kept alive: renewed before it expires,
    /// re-created (with back-off) after errors, and removed when reading stops.
    /// </summary>
    public class CameraEvents : ICameraEvents
    {
        #region Constructor

        private CameraEvents(ICameraInformation camera, IDeviceManagement deviceManagement, ClockSynchronizer clock, CameraEventsOptions options)
        {
            _camera = camera;
            _clock = clock ?? new ClockSynchronizer(camera, deviceManagement);
            _options = options ?? new CameraEventsOptions();
            _eventService = new Lazy<IEventService>(() => EventServiceBuilder.GetBuilder()
                .SetCamera(camera)
                .SetServiceUri(deviceManagement.GetEventServiceUri())
                .Build(), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        #endregion

        #region Private Variables

        private readonly ICameraInformation _camera;
        private readonly ClockSynchronizer _clock;
        private readonly CameraEventsOptions _options;
        private readonly Lazy<IEventService> _eventService;

        #endregion

        #region Public Methods

        public async IAsyncEnumerable<CameraEvent> ReadEventsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            IPullPointSubscription subscription = null;
            var renewAt = DateTimeOffset.MinValue;
            var delay = _options.MinRetryDelay;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    IReadOnlyList<CameraEvent> events = null;
                    Exception error = null;

                    try
                    {
                        if (subscription == null)
                        {
                            subscription = await SubscribeAsync(cancellationToken).ConfigureAwait(false);
                            renewAt = Now() + Half(_options.TerminationTime);
                        }
                        else if (Now() >= renewAt)
                        {
                            await subscription.RenewAsync(_options.TerminationTime, cancellationToken).ConfigureAwait(false);
                            renewAt = Now() + Half(_options.TerminationTime);
                        }

                        events = await subscription.PullMessagesAsync(_options.PullTimeout, _options.MessageLimit, _options.CameraId, cancellationToken).ConfigureAwait(false);
                        delay = _options.MinRetryDelay;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        yield break;
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                    }

                    if (error != null)
                    {
                        // Expired or broken subscription, unreachable device, ...: start over with a new subscription.
                        if (OnvifFaults.IsAuthenticationFailure(error))
                        {
                            await Task.Run(_clock.ResynchronizeAfterAuthenticationFailure, CancellationToken.None).ConfigureAwait(false);
                        }

                        await DropAsync(subscription).ConfigureAwait(false);
                        subscription = null;
                        _options.OnError?.Invoke(error);

                        try
                        {
                            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            yield break;
                        }

                        delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxRetryDelay.Ticks));
                        continue;
                    }

                    foreach (var cameraEvent in events)
                    {
                        yield return cameraEvent;
                    }
                }
            }
            finally
            {
                await DropAsync(subscription).ConfigureAwait(false);
            }
        }

        internal static ICameraEvents CreateInstance(ICameraInformation camera, IDeviceManagement deviceManagement, ClockSynchronizer clock = null, CameraEventsOptions options = null)
        {
            return new CameraEvents(camera, deviceManagement, clock, options);
        }

        #endregion

        #region Private Methods

        private async Task<IPullPointSubscription> SubscribeAsync(CancellationToken cancellationToken)
        {
            await Task.Run(_clock.EnsureSynchronized, cancellationToken).ConfigureAwait(false);
            var service = _eventService.IsValueCreated ? _eventService.Value : await Task.Run(() => _eventService.Value, cancellationToken).ConfigureAwait(false);
            return await service.CreatePullPointSubscriptionAsync(_options.TerminationTime, cancellationToken).ConfigureAwait(false);
        }

        private static async Task DropAsync(IPullPointSubscription subscription)
        {
            if (subscription != null)
            {
                await subscription.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static DateTimeOffset Now()
        {
            return DateTimeOffset.UtcNow;
        }

        private static TimeSpan Half(TimeSpan value)
        {
            return TimeSpan.FromTicks(value.Ticks / 2);
        }

        #endregion
    }

    /// <summary>
    /// Tuning of <see cref="CameraEvents"/>; the defaults suit most devices.
    /// </summary>
    internal sealed class CameraEventsOptions
    {
        #region Public Variables

        /// <summary>Subscription lifetime; renewed at half of it.</summary>
        public TimeSpan TerminationTime { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>Long-poll duration of one PullMessages request.</summary>
        public TimeSpan PullTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public int MessageLimit { get; set; } = 100;

        /// <summary>Copied into every event (the recorder's camera id).</summary>
        public string CameraId { get; set; }
        public TimeSpan MinRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Observes errors that caused a resubscription (for tests and diagnostics).</summary>
        public Action<Exception> OnError { get; set; }

        #endregion
    }
}
