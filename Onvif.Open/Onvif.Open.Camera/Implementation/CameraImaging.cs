// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Core.Imaging;
using Onvif.Open.Imaging.Implementation.Builder;

namespace Onvif.Open.Camera.Implementation
{
    /// <summary>
    /// Image settings and focus of the video source of the camera's first media profile.
    /// </summary>
    public class CameraImaging : ICameraImaging
    {
        #region Constructor

        private CameraImaging(ICameraInformation camera, IDeviceManagement deviceManagement, Func<CancellationToken, Task<IMediaService>> mediaService, ClockSynchronizer clock)
        {
            _clock = clock ?? new ClockSynchronizer(camera, deviceManagement);
            _mediaService = mediaService;
            _imagingService = new Lazy<IImagingService>(() =>
            {
                var uri = deviceManagement.GetImagingServiceUri();
                return uri == null ? null : ImagingServiceBuilder.GetBuilder().SetCamera(camera).SetServiceUri(uri).Build();
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        #endregion

        #region Private Variables

        private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

        private readonly ClockSynchronizer _clock;
        private readonly Func<CancellationToken, Task<IMediaService>> _mediaService;
        private readonly Lazy<IImagingService> _imagingService;
        private readonly SemaphoreSlim _sourceLock = new SemaphoreSlim(1, 1);
        private string _videoSourceToken;

        #endregion

        #region Public Methods

        public Task<ImagingSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync((imaging, source, ct) => imaging.GetImagingSettingsAsync(source, ct), cancellationToken);
        }

        public Task UpdateAsync(ImagingSettings changes, CancellationToken cancellationToken = default)
        {
            if (changes == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            return RunAsync(async (imaging, source, ct) =>
            {
                await imaging.SetImagingSettingsAsync(source, changes, true, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        public Task<ImagingOptions> GetOptionsAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync((imaging, source, ct) => imaging.GetOptionsAsync(source, ct), cancellationToken);
        }

        public async Task MoveFocusAsync(double speed, TimeSpan? duration = null, CancellationToken cancellationToken = default)
        {
            if (double.IsNaN(speed))
            {
                throw new ArgumentOutOfRangeException(nameof(speed));
            }

            speed = Math.Max(-1, Math.Min(1, speed));

            await RunAsync(async (imaging, source, ct) =>
            {
                await imaging.MoveFocusContinuousAsync(source, speed, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);

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
                using (var stop = new CancellationTokenSource(StopTimeout))
                {
                    await StopFocusAsync(stop.Token).ConfigureAwait(false);
                }
            }
        }

        public Task FocusToAsync(double position, CancellationToken cancellationToken = default)
        {
            return RunAsync(async (imaging, source, ct) =>
            {
                await imaging.MoveFocusAbsoluteAsync(source, position, null, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        public Task StopFocusAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync(async (imaging, source, ct) =>
            {
                await imaging.StopFocusAsync(source, ct).ConfigureAwait(false);
                return true;
            }, cancellationToken);
        }

        public Task<FocusStatus> GetFocusStatusAsync(CancellationToken cancellationToken = default)
        {
            return RunAsync((imaging, source, ct) => imaging.GetFocusStatusAsync(source, ct), cancellationToken);
        }

        internal static ICameraImaging CreateInstance(ICameraInformation camera, IDeviceManagement deviceManagement, Func<CancellationToken, Task<IMediaService>> mediaService, ClockSynchronizer clock = null)
        {
            return new CameraImaging(camera, deviceManagement, mediaService, clock);
        }

        #endregion

        #region Private Methods

        private Task<T> RunAsync<T>(Func<IImagingService, string, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            return _clock.RunAsync(async ct =>
            {
                var imaging = _imagingService.IsValueCreated ? _imagingService.Value : await Task.Run(() => _imagingService.Value, ct).ConfigureAwait(false);
                if (imaging == null)
                {
                    throw new NotSupportedException("The camera does not have an Imaging service.");
                }

                var source = await GetVideoSourceTokenAsync(ct).ConfigureAwait(false);
                return await operation(imaging, source, ct).ConfigureAwait(false);
            }, cancellationToken);
        }

        private async Task<string> GetVideoSourceTokenAsync(CancellationToken cancellationToken)
        {
            if (_videoSourceToken != null)
            {
                return _videoSourceToken;
            }

            await _sourceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_videoSourceToken == null)
                {
                    var media = await _mediaService(cancellationToken).ConfigureAwait(false);
                    var profiles = await media.GetProfilesAsync(cancellationToken).ConfigureAwait(false);

                    _videoSourceToken = profiles.Select(p => p.VideoSourceToken).FirstOrDefault(t => !string.IsNullOrEmpty(t))
                                        ?? throw new InvalidOperationException("No media profile reports a video source.");
                }

                return _videoSourceToken;
            }
            finally
            {
                _sourceLock.Release();
            }
        }

        #endregion
    }
}
