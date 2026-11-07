// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.Core.Abstract.Interface.Events;
using Onvif.Open.Core.DeviceIo;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Operations;
using Onvif.Open.DeviceIo.Implementation.Builder;

namespace Onvif.Open.Camera.Implementation
{
    /// <summary>
    /// Relay outputs (device service) and digital inputs (DeviceIO service and events) of a camera.
    /// </summary>
    public class CameraIo : ICameraIo
    {
        #region Constructor

        private CameraIo(ICameraInformation camera, IDeviceManagement deviceManagement, ICameraEvents events, ClockSynchronizer clock)
        {
            _deviceManagement = deviceManagement;
            _events = events;
            _clock = clock ?? new ClockSynchronizer(camera, deviceManagement);
            _deviceIo = new Lazy<IDeviceIoService>(() =>
            {
                var uri = deviceManagement.FindServiceUri(OnvifServiceNamespaces.DeviceIo);
                return uri == null ? null : DeviceIoServiceBuilder.GetBuilder().SetCamera(camera).SetServiceUri(uri).Build();
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        #endregion

        #region Private Variables

        /// <summary>Topic of digital input state changes (ONVIF Core Specification).</summary>
        public const string DigitalInputTopic = "Device/Trigger/DigitalInput";

        private static readonly TimeSpan DeactivateTimeout = TimeSpan.FromSeconds(5);

        private readonly IDeviceManagement _deviceManagement;
        private readonly ICameraEvents _events;
        private readonly ClockSynchronizer _clock;
        private readonly Lazy<IDeviceIoService> _deviceIo;

        #endregion

        #region Public Methods

        public Task<IReadOnlyList<RelayOutput>> GetRelayOutputsAsync(CancellationToken cancellationToken = default)
        {
            return _clock.RunAsync(ct => Task.Run(() => _deviceManagement.GetRelayOutputs(), ct), cancellationToken);
        }

        public Task SetRelayAsync(string relayToken, bool active, CancellationToken cancellationToken = default)
        {
            Require(relayToken);
            return _clock.RunAsync(ct => Task.Run(() => _deviceManagement.SetRelayOutputState(relayToken, active), ct), cancellationToken);
        }

        public async Task PulseRelayAsync(string relayToken, TimeSpan duration, CancellationToken cancellationToken = default)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            await SetRelayAsync(relayToken, true, cancellationToken).ConfigureAwait(false);

            try
            {
                await Task.Delay(duration, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Never leave a door opener or siren on because the caller cancelled.
                using (var deactivate = new CancellationTokenSource(DeactivateTimeout))
                {
                    await SetRelayAsync(relayToken, false, deactivate.Token).ConfigureAwait(false);
                }
            }
        }

        public Task ConfigureRelayAsync(string relayToken, RelayMode mode, TimeSpan delayTime, IdleState idleState, CancellationToken cancellationToken = default)
        {
            Require(relayToken);

            if (delayTime < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delayTime));
            }

            return _clock.RunAsync(ct => Task.Run(() => _deviceManagement.SetRelayOutputSettings(relayToken, mode, delayTime, idleState), ct), cancellationToken);
        }

        public Task<IReadOnlyList<DigitalInput>> GetDigitalInputsAsync(CancellationToken cancellationToken = default)
        {
            return _clock.RunAsync(async ct =>
            {
                var service = _deviceIo.IsValueCreated ? _deviceIo.Value : await Task.Run(() => _deviceIo.Value, ct).ConfigureAwait(false);
                if (service == null)
                {
                    throw new NotSupportedException("The camera does not have a DeviceIO service.");
                }

                return await service.GetDigitalInputsAsync(ct).ConfigureAwait(false);
            }, cancellationToken);
        }

        public async IAsyncEnumerable<IoStateChange> WatchDigitalInputsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var cameraEvent in _events.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
            {
                var change = ToDigitalInputChange(cameraEvent);
                if (change != null)
                {
                    yield return change;
                }
            }
        }

        /// <summary>Maps a Device/Trigger/DigitalInput event; null for any other event.</summary>
        public static IoStateChange ToDigitalInputChange(CameraEvent cameraEvent)
        {
            if (cameraEvent == null || !cameraEvent.MatchesTopic(DigitalInputTopic))
            {
                return null;
            }

            var state = cameraEvent.GetState();
            if (!state.HasValue)
            {
                return null;
            }

            var token = cameraEvent.Source.TryGetValue("InputToken", out var input) ? input : cameraEvent.Source.Values.FirstOrDefault();
            return new IoStateChange(token, state.Value, cameraEvent.IsInitialState, cameraEvent.UtcTime);
        }

        internal static ICameraIo CreateInstance(ICameraInformation camera, IDeviceManagement deviceManagement, ICameraEvents events, ClockSynchronizer clock = null)
        {
            return new CameraIo(camera, deviceManagement, events, clock);
        }

        #endregion

        #region Private Methods

        private static void Require(string relayToken)
        {
            if (string.IsNullOrWhiteSpace(relayToken))
            {
                throw new ArgumentNullException(nameof(relayToken));
            }
        }

        #endregion
    }
}
