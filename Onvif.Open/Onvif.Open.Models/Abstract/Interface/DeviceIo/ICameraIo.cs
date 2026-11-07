// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.DeviceIo;

namespace Onvif.Open.Core.Abstract.Interface.DeviceIo
{
    /// <summary>
    /// Relay outputs (sirens, door openers, lights) and digital inputs (door contacts, PIR sensors) of a camera.
    /// </summary>
    public interface ICameraIo
    {
        #region Public Methods

        Task<IReadOnlyList<RelayOutput>> GetRelayOutputsAsync(CancellationToken cancellationToken = default);

        /// <summary>Activates or deactivates a relay (device Ver10 SetRelayOutputState).</summary>
        Task SetRelayAsync(string relayToken, bool active, CancellationToken cancellationToken = default);

        /// <summary>Activates a relay for <paramref name="duration"/>, then deactivates it (always, even when cancelled).</summary>
        Task PulseRelayAsync(string relayToken, TimeSpan duration, CancellationToken cancellationToken = default);

        Task ConfigureRelayAsync(string relayToken, RelayMode mode, TimeSpan delayTime, IdleState idleState, CancellationToken cancellationToken = default);

        /// <summary>
        /// Digital inputs (DeviceIO service). Throws <see cref="NotSupportedException"/> when the device has no DeviceIO service.
        /// </summary>
        Task<IReadOnlyList<DigitalInput>> GetDigitalInputsAsync(CancellationToken cancellationToken = default);

        /// <summary>Digital input changes from the device's events (topic Device/Trigger/DigitalInput), until cancelled.</summary>
        IAsyncEnumerable<IoStateChange> WatchDigitalInputsAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
