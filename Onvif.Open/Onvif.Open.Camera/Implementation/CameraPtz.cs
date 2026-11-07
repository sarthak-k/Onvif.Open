// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Core.Ptz;
using Onvif.Open.Ptz.Implementation.Builder;

namespace Onvif.Open.Camera.Implementation
{
    /// <summary>
    /// Pan/tilt/zoom on the camera's first PTZ-enabled media profile, with the device clock kept in sync.
    /// </summary>
    public class CameraPtz : ICameraPtz
    {
        #region Constructor

        private CameraPtz(ICameraInformation camera, IDeviceManagement deviceManagement, Func<CancellationToken, Task<IMediaService>> mediaService, ClockSynchronizer clock)
        {
            _clock = clock ?? new ClockSynchronizer(camera, deviceManagement);
            _mediaService = mediaService;
            _ptzService = new Lazy<IPtzService>(() =>
            {
                var uri = deviceManagement.GetPtzServiceUri();
                return uri == null ? null : PtzServiceBuilder.GetBuilder().SetCamera(camera).SetServiceUri(uri).Build();
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        #endregion

        #region Private Variables

        /// <summary>Stop is sent even when the caller cancels a timed move, within this time.</summary>
        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        private readonly ClockSynchronizer _clock;
        private readonly Func<CancellationToken, Task<IMediaService>> _mediaService;
        private readonly Lazy<IPtzService> _ptzService;
        private readonly SemaphoreSlim _profileLock = new SemaphoreSlim(1, 1);
        private string _profileToken;

        #endregion

        #region Public Methods

        public async Task MoveAsync(double pan, double tilt, double zoom, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            pan = Clamp(pan);
            tilt = Clamp(tilt);
            zoom = Clamp(zoom);

            if (pan == 0 && tilt == 0 && zoom == 0)
            {
                await StopAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            // Only the axes that move are sent: some devices reject zoom on cameras without zoom.
            var velocity = new PtzVector(
                pan != 0 || tilt != 0 ? pan : (double?)null,
                pan != 0 || tilt != 0 ? tilt : (double?)null,
                zoom != 0 ? zoom : (double?)null);

            await RunAsync((ptz, profile, ct) => ptz.ContinuousMoveAsync(profile, velocity, duration, ct), cancellationToken).ConfigureAwait(false);

            if (!duration.HasValue)
            {
                return;
            }

            try
            {
                await Task.Delay(duration.Value, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Devices do not all honour the ContinuousMove timeout: always stop explicitly.
                using (var stop = new CancellationTokenSource(StopTimeout))
                {
                    await StopAsync(stop.Token).ConfigureAwait(false);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync((ptz, profile, ct) => ptz.StopAsync(profile, true, true, ct), cancellationToken);
        }

        public Task MoveByAsync(PtzVector translation, CancellationToken cancellationToken = default)
        {
            return RunAsync((ptz, profile, ct) => ptz.RelativeMoveAsync(profile, translation ?? throw new ArgumentNullException(nameof(translation)), null, ct), cancellationToken);
        }

        public Task MoveToAsync(PtzVector position, CancellationToken cancellationToken = default)
        {
            return RunAsync((ptz, profile, ct) => ptz.AbsoluteMoveAsync(profile, position ?? throw new ArgumentNullException(nameof(position)), null, ct), cancellationToken);
        }

        public Task<PtzStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync((ptz, profile, ct) => ptz.GetStatusAsync(profile, ct), cancellationToken);
        }

        public Task<IReadOnlyList<PtzPreset>> GetPresetsAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync((ptz, profile, ct) => ptz.GetPresetsAsync(profile, ct), cancellationToken);
        }

        public Task GoToPresetAsync(string nameOrToken, CancellationToken cancellationToken = default)
        {
            return RunAsync(async (ptz, profile, ct) =>
            {
                var preset = Find(await ptz.GetPresetsAsync(profile, ct).ConfigureAwait(false), nameOrToken)
                             ?? throw new KeyNotFoundException($"The camera has no preset '{nameOrToken}'.");
                await ptz.GotoPresetAsync(profile, preset.Token, null, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        public Task<string> SavePresetAsync(string name, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            return RunAsync(async (ptz, profile, ct) =>
            {
                var existing = (await ptz.GetPresetsAsync(profile, ct).ConfigureAwait(false))
                    .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                // Overwrite a preset with the same name (case-insensitive) and keep its original spelling.
                return await ptz.SetPresetAsync(profile, existing?.Name ?? name, existing?.Token, ct).ConfigureAwait(false);
            }, cancellationToken);
        }

        public Task<bool> RemovePresetAsync(string nameOrToken, CancellationToken cancellationToken = default)
        {
            return RunAsync(async (ptz, profile, ct) =>
            {
                var preset = Find(await ptz.GetPresetsAsync(profile, ct).ConfigureAwait(false), nameOrToken);
                if (preset == null)
                {
                    return false;
                }

                await ptz.RemovePresetAsync(profile, preset.Token, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        public Task GoHomeAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync((ptz, profile, ct) => ptz.GotoHomePositionAsync(profile, null, ct), cancellationToken);
        }

        public Task SetHomeAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync((ptz, profile, ct) => ptz.SetHomePositionAsync(profile, ct), cancellationToken);
        }

        internal static ICameraPtz CreateInstance(ICameraInformation camera, IDeviceManagement deviceManagement, Func<CancellationToken, Task<IMediaService>> mediaService, ClockSynchronizer clock = null)
        {
            return new CameraPtz(camera, deviceManagement, mediaService, clock);
        }

        #endregion

        #region Private Methods

        private Task RunAsync(Func<IPtzService, string, CancellationToken, Task> operation, CancellationToken cancellationToken)
        {
            return RunAsync(async (ptz, profile, ct) =>
            {
                await operation(ptz, profile, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        private Task<T> RunAsync<T>(Func<IPtzService, string, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            return _clock.RunAsync(async ct =>
            {
                var ptz = await GetPtzServiceAsync(ct).ConfigureAwait(false);
                var profile = await GetProfileTokenAsync(ct).ConfigureAwait(false);
                return await operation(ptz, profile, ct).ConfigureAwait(false);
            }, cancellationToken);
        }

        private async Task<IPtzService> GetPtzServiceAsync(CancellationToken cancellationToken)
        {
            var service = _ptzService.IsValueCreated ? _ptzService.Value : await Task.Run(() => _ptzService.Value, cancellationToken).ConfigureAwait(false);
            return service ?? throw new NotSupportedException("The camera does not support PTZ.");
        }

        private async Task<string> GetProfileTokenAsync(CancellationToken cancellationToken)
        {
            if (_profileToken != null)
            {
                return _profileToken;
            }

            await _profileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_profileToken == null)
                {
                    var media = await _mediaService(cancellationToken).ConfigureAwait(false);
                    var profiles = await media.GetProfilesAsync(cancellationToken).ConfigureAwait(false);

                    _profileToken = (profiles.FirstOrDefault(p => p.PtzConfigurationToken != null) ?? profiles.FirstOrDefault())?.Token
                                    ?? throw new InvalidOperationException("The device does not expose any media profile.");
                }

                return _profileToken;
            }
            finally
            {
                _profileLock.Release();
            }
        }

        private static PtzPreset Find(IEnumerable<PtzPreset> presets, string nameOrToken)
        {
            if (string.IsNullOrWhiteSpace(nameOrToken))
            {
                throw new ArgumentNullException(nameof(nameOrToken));
            }

            var list = presets.ToList();
            return list.FirstOrDefault(p => p.Token == nameOrToken)
                   ?? list.FirstOrDefault(p => string.Equals(p.Name, nameOrToken, StringComparison.OrdinalIgnoreCase));
        }

        private static double Clamp(double value)
        {
            if (double.IsNaN(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return Math.Max(-1, Math.Min(1, value));
        }

        #endregion
    }
}
