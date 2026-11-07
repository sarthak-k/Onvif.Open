// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Core.Operations;
using Onvif.Open.Media.Implementation.Builder;
using Onvif.Open.Media.Ver10;

namespace Onvif.Open.Media.Implementation.Client
{
    /// <summary>
    /// Client for the ONVIF Media (ver10) service.
    /// </summary>
    public sealed class MediaService : IMediaService
    {
        #region Constructor

        private MediaService(Uri serviceUri, ICameraInformation camera)
        {
            ServiceUri = serviceUri;
            _camera = camera;
        }

        #endregion

        #region Private Variables

        private readonly ICameraInformation _camera;

        #endregion

        #region Public Variables

        public Uri ServiceUri { get; }

        public MediaServiceVersion Version => MediaServiceVersion.Media1;

        #endregion

        #region Public Methods

        public IReadOnlyList<IMediaProfile> GetProfiles()
        {
            return Invoke(port => MapProfiles(port.GetProfiles(new GetProfilesRequest())));
        }

        public Task<IReadOnlyList<IMediaProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
        {
            return InvokeAsync(async port => MapProfiles(await port.GetProfilesAsync(new GetProfilesRequest()).ConfigureAwait(false)), cancellationToken);
        }

        public Uri GetStreamUri(string profileToken, StreamTransport transport = StreamTransport.Rtsp)
        {
            var request = CreateStreamUriRequest(profileToken, transport);
            return Invoke(port => ParseUri(port.GetStreamUri(request).MediaUri));
        }

        public Task<Uri> GetStreamUriAsync(string profileToken, StreamTransport transport = StreamTransport.Rtsp, CancellationToken cancellationToken = default)
        {
            var request = CreateStreamUriRequest(profileToken, transport);
            return InvokeAsync(async port => ParseUri((await port.GetStreamUriAsync(request).ConfigureAwait(false)).MediaUri), cancellationToken);
        }

        public Uri GetSnapshotUri(string profileToken)
        {
            var request = new GetSnapshotUriRequest { ProfileToken = RequireToken(profileToken) };
            return Invoke(port => ParseUri(port.GetSnapshotUri(request).MediaUri));
        }

        public Task<Uri> GetSnapshotUriAsync(string profileToken, CancellationToken cancellationToken = default)
        {
            var request = new GetSnapshotUriRequest { ProfileToken = RequireToken(profileToken) };
            return InvokeAsync(async port => ParseUri((await port.GetSnapshotUriAsync(request).ConfigureAwait(false)).MediaUri), cancellationToken);
        }

        internal static IMediaService CreateInstance(Uri serviceUri, ICameraInformation camera)
        {
            return new MediaService(serviceUri, camera);
        }

        #endregion

        #region Private Methods

        private static GetStreamUriRequest CreateStreamUriRequest(string profileToken, StreamTransport transport)
        {
            return new GetStreamUriRequest
            {
                ProfileToken = RequireToken(profileToken),
                StreamSetup = new StreamSetup
                {
                    Stream = "RTP-Unicast",
                    Transport = new Transport { Protocol = ToProtocol(transport) }
                }
            };
        }

        private static string ToProtocol(StreamTransport transport)
        {
            switch (transport)
            {
                case StreamTransport.Udp:
                    return "UDP";
                case StreamTransport.Http:
                    return "HTTP";
                default:
                    return "RTSP";
            }
        }

        private static string RequireToken(string profileToken)
        {
            if (string.IsNullOrWhiteSpace(profileToken))
            {
                throw new ArgumentNullException(nameof(profileToken));
            }

            return profileToken;
        }

        private static IReadOnlyList<IMediaProfile> MapProfiles(GetProfilesResponse response)
        {
            return (response?.Profiles ?? Array.Empty<Profile>()).Select(MediaProfile.CreateInstance).ToList();
        }

        private static Uri ParseUri(MediaUri mediaUri)
        {
            return ParseUri(mediaUri?.Uri);
        }

        internal static Uri ParseUri(string mediaUri)
        {
            var value = mediaUri?.Trim();

            if (string.IsNullOrEmpty(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException($"Device returned an invalid media URI: '{value}'.");
            }

            return uri;
        }

        private MediaClient CreateClient()
        {
            var authenticator = PasswordDigestBuilder.GetBuilder().SetCamera(_camera).Build();

            return MediaClientBuilder.GetBuilder()
                .SetHttpTransportBinding()
                .SetMessageElement()
                .SetEndpoint(new EndpointAddress(ServiceUri))
                .SetCustomeBinding()
                .SetClient()
                .SetAuthenticator(authenticator)
                .Build();
        }

        private T Invoke<T>(Func<IMediaPort, T> call)
        {
            return WcfInvoker.Invoke(CreateClient, client => call(client.Port));
        }

        private Task<T> InvokeAsync<T>(Func<IMediaPort, Task<T>> call, CancellationToken cancellationToken)
        {
            return WcfInvoker.InvokeAsync(CreateClient, client => call(client.Port), cancellationToken);
        }

        #endregion
    }
}
