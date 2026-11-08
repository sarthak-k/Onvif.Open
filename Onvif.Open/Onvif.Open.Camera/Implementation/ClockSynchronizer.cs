// Sarthak Killedar
// 2026-11-07

using System;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Core.Implementation.Security;

namespace Onvif.Open.Camera.Implementation
{
    /// <summary>
    /// Keeps a camera's <see cref="DeviceClock"/> up to date: measured before the first secured call,
    /// refreshed when older than <see cref="MaxAge"/>, and re-measured when the device rejects a token.
    /// </summary>
    internal sealed class ClockSynchronizer
    {
        #region Constructor

        public ClockSynchronizer(ICameraInformation camera, IDeviceManagement deviceManagement, TimeProvider timeProvider = null)
        {
            _clock = DeviceClock.For(camera);
            _deviceManagement = deviceManagement;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Private Variables

        /// <summary>Re-measure after this long: camera clocks without NTP drift by seconds per day.</summary>
        internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

        /// <summary>Do not hammer a device that fails to answer GetSystemDateAndTime.</summary>
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        /// <summary>Offsets closer than this to the current one do not explain an authentication failure.</summary>
        private static readonly TimeSpan SignificantChange = TimeSpan.FromSeconds(2);

        private readonly DeviceClock _clock;
        private readonly IDeviceManagement _deviceManagement;
        private readonly TimeProvider _timeProvider;
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();
        private DateTimeOffset? _lastAttempt;

        #endregion

        #region Public Variables

        public TimeSpan Offset => _clock.Offset;

        #endregion

        #region Public Methods

        /// <summary>Measures the offset now; throws when the device cannot be reached.</summary>
        public TimeSpan Synchronize()
        {
            lock (_sync)
            {
                var now = _timeProvider.GetUtcNow();
                _lastAttempt = now;

                var offset = _deviceManagement.GetClockOffset() ?? TimeSpan.Zero;
                _clock.Update(offset, now);
                return offset;
            }
        }

        /// <summary>Measures the offset when it was never measured or is older than <see cref="MaxAge"/>. Never throws.</summary>
        public void EnsureSynchronized()
        {
            var now = _timeProvider.GetUtcNow();
            var last = _clock.LastSynchronized;

            if (last.HasValue && now - last.Value < MaxAge)
            {
                return;
            }

            lock (_sync)
            {
                if (_lastAttempt.HasValue && now - _lastAttempt.Value < RetryDelay)
                {
                    return;
                }
            }

            TrySynchronize(out _);
        }

        /// <summary>
        /// After the device rejected a token: re-measures and returns true when the offset changed enough
        /// that retrying makes sense (a wrong password does not change the clock).
        /// </summary>
        public bool ResynchronizeAfterAuthenticationFailure()
        {
            var previous = _clock.Offset;
            return TrySynchronize(out var offset) && (offset - previous).Duration() >= SignificantChange;
        }

        /// <summary>
        /// Runs a secured operation with the clock synchronized; when the device rejects the token because its
        /// clock moved, re-synchronizes and retries once.
        /// </summary>
        public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            await Task.Run(EnsureSynchronized, cancellationToken).ConfigureAwait(false);

            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (OnvifFaults.IsAuthenticationFailure(ex))
            {
                if (!await Task.Run(ResynchronizeAfterAuthenticationFailure, cancellationToken).ConfigureAwait(false))
                {
                    throw;
                }

                return await operation(cancellationToken).ConfigureAwait(false);
            }
        }

        public Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            return RunAsync(async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        #endregion

        #region Private Methods

        private bool TrySynchronize(out TimeSpan offset)
        {
            try
            {
                offset = Synchronize();
                return true;
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException) && !(ex is ThreadAbortException))
            {
                // Keep the previous offset; the actual call reports the real problem if there is one.
                offset = _clock.Offset;
                return false;
            }
        }

        #endregion
    }
}
