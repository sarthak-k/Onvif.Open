// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Core.Operations;
using Onvif.Open.Media.Implementation.Builder;
using Onvif.Open.Streaming.Implementation.Builder;

namespace Onvif.Open.Camera.Implementation
{
    public class CameraMedia : ICameraMedia
    {
        #region Constructor

        private CameraMedia(ICameraInformation camera, IDeviceManagement deviceManagement, ClockSynchronizer clock)
        {
            _camera = camera;
            _clock = clock ?? new ClockSynchronizer(camera, deviceManagement);
            _mediaService = new Lazy<IMediaService>(() => MediaServiceBuilder.GetBuilder()
                .SetCamera(_camera)
                .SetServiceUri(deviceManagement.GetMediaServiceUri())
                .Build(), LazyThreadSafetyMode.ExecutionAndPublication);
            _snapshotClient = new Lazy<HttpClient>(CreateSnapshotClient, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        #endregion

        #region Private Variables

        private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(10);

        private readonly ICameraInformation _camera;
        private readonly ClockSynchronizer _clock;
        private readonly Lazy<IMediaService> _mediaService;
        private readonly Lazy<HttpClient> _snapshotClient;

        #endregion

        #region Public Methods

        public IMediaService GetMediaService()
        {
            _clock.EnsureSynchronized();
            return _mediaService.Value;
        }

        public Task<byte[]> GetSnapshotAsync(string profileToken = null, CancellationToken cancellationToken = default)
        {
            return _clock.RunAsync(ct => GetSnapshotCoreAsync(profileToken, ct), cancellationToken);
        }

        public Task<ILiveVideoStream> StartLiveStreamAsync(string profileToken = null, StreamTransport transport = StreamTransport.Rtsp, CancellationToken cancellationToken = default)
        {
            return _clock.RunAsync(ct => StartLiveStreamCoreAsync(profileToken, transport, ct), cancellationToken);
        }

        private async Task<byte[]> GetSnapshotCoreAsync(string profileToken, CancellationToken cancellationToken)
        {
            var mediaService = await GetMediaServiceAsync(cancellationToken).ConfigureAwait(false);
            profileToken = await ResolveProfileAsync(mediaService, profileToken, cancellationToken).ConfigureAwait(false);

            var snapshotUri = await mediaService.GetSnapshotUriAsync(profileToken, cancellationToken).ConfigureAwait(false);

            using (var response = await _snapshotClient.Value.GetAsync(snapshotUri, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Snapshot request failed: {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
                }

                return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<ILiveVideoStream> StartLiveStreamCoreAsync(string profileToken, StreamTransport transport, CancellationToken cancellationToken)
        {
            var mediaService = await GetMediaServiceAsync(cancellationToken).ConfigureAwait(false);
            profileToken = await ResolveProfileAsync(mediaService, profileToken, cancellationToken).ConfigureAwait(false);

            var streamUri = await mediaService.GetStreamUriAsync(profileToken, transport, cancellationToken).ConfigureAwait(false);

            return await LiveVideoStreamBuilder.GetBuilder()
                .SetStreamUri(streamUri)
                .SetCamera(_camera)
                .SetTransport(transport)
                .BuildAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        internal static ICameraMedia CreateInstance(ICameraInformation camera, IDeviceManagement deviceManagement, ClockSynchronizer clock = null)
        {
            return new CameraMedia(camera, deviceManagement, clock);
        }

        #endregion

        #region Private Methods

        internal async Task<IMediaService> GetMediaServiceAsync(CancellationToken cancellationToken)
        {
            // Resolving the media service address is a synchronous device call; keep it off the caller's thread.
            return _mediaService.IsValueCreated
                ? _mediaService.Value
                : await Task.Run(() => _mediaService.Value, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<string> ResolveProfileAsync(IMediaService mediaService, string profileToken, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(profileToken))
            {
                return profileToken;
            }

            var profiles = await mediaService.GetProfilesAsync(cancellationToken).ConfigureAwait(false);
            return profiles.FirstOrDefault()?.Token
                   ?? throw new InvalidOperationException("The device does not expose any media profile.");
        }

        private HttpClient CreateSnapshotClient()
        {
            // Devices protect snapshots with HTTP Basic or Digest; HttpClientHandler answers either challenge.
            var handler = new HttpClientHandler();

            if (!string.IsNullOrEmpty(_camera.Username))
            {
                handler.Credentials = new NetworkCredential(_camera.Username, _camera.Password ?? string.Empty);
            }

            return new HttpClient(handler) { Timeout = SnapshotTimeout };
        }

        #endregion
    }
}
