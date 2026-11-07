// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Operations;
using Onvif.Open.Media.Implementation.Builder;
using Onvif.Open.Streaming.Implementation.Builder;

namespace Onvif.Open.Camera.Implementation
{
    public class CameraMedia : ICameraMedia
    {
        #region Constructor

        private CameraMedia(ICameraInformation camera, IDeviceManagement deviceManagement)
        {
            _camera = camera;
            _mediaService = new Lazy<IMediaService>(() => MediaServiceBuilder.GetBuilder()
                .SetCamera(_camera)
                .SetServiceUri(deviceManagement.GetMediaServiceUri())
                .Build(), LazyThreadSafetyMode.ExecutionAndPublication);
        }

        #endregion

        #region Private Variables

        private readonly ICameraInformation _camera;
        private readonly Lazy<IMediaService> _mediaService;

        #endregion

        #region Public Methods

        public IMediaService GetMediaService()
        {
            return _mediaService.Value;
        }

        public async Task<ILiveVideoStream> StartLiveStreamAsync(string profileToken = null, CancellationToken cancellationToken = default)
        {
            // Resolving the media service address is a synchronous device call; keep it off the caller's thread.
            var mediaService = _mediaService.IsValueCreated
                ? _mediaService.Value
                : await Task.Run(() => _mediaService.Value, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(profileToken))
            {
                var profiles = await mediaService.GetProfilesAsync(cancellationToken).ConfigureAwait(false);
                profileToken = profiles.FirstOrDefault()?.Token
                               ?? throw new InvalidOperationException("The device does not expose any media profile.");
            }

            var streamUri = await mediaService.GetStreamUriAsync(profileToken, StreamTransport.Rtsp, cancellationToken).ConfigureAwait(false);

            return await LiveVideoStreamBuilder.GetBuilder()
                .SetStreamUri(streamUri)
                .SetCamera(_camera)
                .BuildAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        internal static ICameraMedia CreateInstance(ICameraInformation camera, IDeviceManagement deviceManagement)
        {
            return new CameraMedia(camera, deviceManagement);
        }

        #endregion
    }
}
