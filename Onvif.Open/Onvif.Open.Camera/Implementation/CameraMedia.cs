// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Analytics;
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

        private CameraMedia(ICameraInformation camera, IDeviceManagement deviceManagement, ClockSynchronizer clock, MediaServiceVersion version)
        {
            _camera = camera;
            _clock = clock ?? new ClockSynchronizer(camera, deviceManagement);
            _mediaService = new Lazy<IMediaService>(() => ResolveMediaService(camera, deviceManagement, version), LazyThreadSafetyMode.ExecutionAndPublication);
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
            return StartLiveStreamAsync(profileToken, transport, false, cancellationToken);
        }

        public Task<ILiveVideoStream> StartLiveStreamAsync(string profileToken, StreamTransport transport, bool includeAudio, CancellationToken cancellationToken = default)
        {
            return _clock.RunAsync(ct => StartLiveStreamCoreAsync(profileToken, transport, includeAudio, ct), cancellationToken);
        }

        public Task<IAnalyticsStream> StartAnalyticsStreamAsync(string profileToken = null, StreamTransport transport = StreamTransport.Rtsp, CancellationToken cancellationToken = default)
        {
            return _clock.RunAsync(ct => StartAnalyticsStreamCoreAsync(profileToken, transport, ct), cancellationToken);
        }

        private async Task<IAnalyticsStream> StartAnalyticsStreamCoreAsync(string profileToken, StreamTransport transport, CancellationToken cancellationToken)
        {
            var mediaService = await GetMediaServiceAsync(cancellationToken).ConfigureAwait(false);

            if (profileToken == null)
            {
                // The metadata track is part of a profile's stream only when the profile has a metadata configuration.
                var profiles = await mediaService.GetProfilesAsync(cancellationToken).ConfigureAwait(false);
                profileToken = profiles.FirstOrDefault(p => p.MetadataConfigurationToken != null)?.Token
                               ?? throw new NotSupportedException("No media profile of the camera has a metadata configuration: enable analytics metadata on the camera (ONVIF Profile M).");
            }

            var streamUri = await mediaService.GetStreamUriAsync(profileToken, transport, cancellationToken).ConfigureAwait(false);

            return await LiveVideoStreamBuilder.GetBuilder()
                .SetStreamUri(streamUri)
                .SetCamera(_camera)
                .SetTransport(transport)
                .BuildAnalyticsAsync(cancellationToken)
                .ConfigureAwait(false);
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

        private async Task<ILiveVideoStream> StartLiveStreamCoreAsync(string profileToken, StreamTransport transport, bool includeAudio, CancellationToken cancellationToken)
        {
            var mediaService = await GetMediaServiceAsync(cancellationToken).ConfigureAwait(false);
            profileToken = await ResolveProfileAsync(mediaService, profileToken, cancellationToken).ConfigureAwait(false);

            var streamUri = await mediaService.GetStreamUriAsync(profileToken, transport, cancellationToken).ConfigureAwait(false);

            return await LiveVideoStreamBuilder.GetBuilder()
                .SetStreamUri(streamUri)
                .SetCamera(_camera)
                .SetTransport(transport)
                .SetAudio(includeAudio)
                .BuildAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        internal static ICameraMedia CreateInstance(ICameraInformation camera, IDeviceManagement deviceManagement, ClockSynchronizer clock = null, MediaServiceVersion version = MediaServiceVersion.Auto)
        {
            return new CameraMedia(camera, deviceManagement, clock, version);
        }

        /// <summary>
        /// Media2 when the device offers it (and Media1 was not requested), otherwise Media1.
        /// </summary>
        internal static IMediaService ResolveMediaService(ICameraInformation camera, IDeviceManagement deviceManagement, MediaServiceVersion version)
        {
            var builder = MediaServiceBuilder.GetBuilder().SetCamera(camera);

            if (version != MediaServiceVersion.Media1)
            {
                var media2 = deviceManagement.FindServiceUri(OnvifServiceNamespaces.Media2);

                if (media2 != null)
                {
                    return builder.SetVersion(MediaServiceVersion.Media2).SetServiceUri(media2).Build();
                }

                if (version == MediaServiceVersion.Media2)
                {
                    throw new NotSupportedException("The device does not offer the Media2 service.");
                }
            }

            return builder.SetVersion(MediaServiceVersion.Media1).SetServiceUri(deviceManagement.GetMediaServiceUri()).Build();
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
            // Connects only where the camera network rule allows; no redirects (they could lead elsewhere).
            var handler = Onvif.Open.Core.Networking.CameraNetwork.CreateHttpHandler();
            handler.AllowAutoRedirect = false;

            if (!string.IsNullOrEmpty(_camera.Username))
            {
                handler.Credentials = new NetworkCredential(_camera.Username, _camera.Password ?? string.Empty);
            }

            return new HttpClient(handler) { Timeout = SnapshotTimeout };
        }

        #endregion
    }
}
