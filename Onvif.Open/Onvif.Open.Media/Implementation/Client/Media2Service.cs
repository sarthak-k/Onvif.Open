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
using Onvif.Open.Media.Ver20;

namespace Onvif.Open.Media.Implementation.Client
{
    /// <summary>
    /// Client for the ONVIF Media2 (ver20) service: the same operations as Media1, with H.265-aware profiles.
    /// </summary>
    public sealed class Media2Service : IMediaService
    {
        #region Constructor

        private Media2Service(Uri serviceUri, ICameraInformation camera)
        {
            ServiceUri = serviceUri;
            _camera = camera;
        }

        #endregion

        #region Private Variables

        private static readonly string[] AllConfigurations = { "All" };

        private readonly ICameraInformation _camera;

        #endregion

        #region Public Variables

        public Uri ServiceUri { get; }

        public MediaServiceVersion Version => MediaServiceVersion.Media2;

        #endregion

        #region Public Methods

        public IReadOnlyList<IMediaProfile> GetProfiles()
        {
            return WcfInvoker.Invoke(CreateClient, c => MapProfiles(c.Port.GetProfiles(new GetProfiles2Request { Type = AllConfigurations })));
        }

        public Task<IReadOnlyList<IMediaProfile>> GetProfilesAsync(CancellationToken cancellationToken = default)
        {
            return WcfInvoker.InvokeAsync(CreateClient, async c => MapProfiles(await c.Port.GetProfilesAsync(new GetProfiles2Request { Type = AllConfigurations }).ConfigureAwait(false)), cancellationToken);
        }

        public Uri GetStreamUri(string profileToken, StreamTransport transport = StreamTransport.Rtsp)
        {
            var request = CreateStreamUriRequest(profileToken, transport);
            return WcfInvoker.Invoke(CreateClient, c => MediaService.ParseUri(c.Port.GetStreamUri(request).Uri));
        }

        public Task<Uri> GetStreamUriAsync(string profileToken, StreamTransport transport = StreamTransport.Rtsp, CancellationToken cancellationToken = default)
        {
            var request = CreateStreamUriRequest(profileToken, transport);
            return WcfInvoker.InvokeAsync(CreateClient, async c => MediaService.ParseUri((await c.Port.GetStreamUriAsync(request).ConfigureAwait(false)).Uri), cancellationToken);
        }

        public Uri GetSnapshotUri(string profileToken)
        {
            var request = new GetSnapshotUri2Request { ProfileToken = RequireToken(profileToken) };
            return WcfInvoker.Invoke(CreateClient, c => MediaService.ParseUri(c.Port.GetSnapshotUri(request).Uri));
        }

        public Task<Uri> GetSnapshotUriAsync(string profileToken, CancellationToken cancellationToken = default)
        {
            var request = new GetSnapshotUri2Request { ProfileToken = RequireToken(profileToken) };
            return WcfInvoker.InvokeAsync(CreateClient, async c => MediaService.ParseUri((await c.Port.GetSnapshotUriAsync(request).ConfigureAwait(false)).Uri), cancellationToken);
        }

        internal static IMediaService CreateInstance(Uri serviceUri, ICameraInformation camera)
        {
            return new Media2Service(serviceUri, camera);
        }

        /// <summary>Media2 stream protocols (ver20 media, GetStreamUri).</summary>
        internal static string ToProtocol(StreamTransport transport)
        {
            switch (transport)
            {
                case StreamTransport.Udp:
                    return "RtspUnicast";
                case StreamTransport.Http:
                    return "RtspOverHttp";
                default:
                    return "RTSP";
            }
        }

        #endregion

        #region Private Methods

        private static GetStreamUri2Request CreateStreamUriRequest(string profileToken, StreamTransport transport)
        {
            return new GetStreamUri2Request { ProfileToken = RequireToken(profileToken), Protocol = ToProtocol(transport) };
        }

        private static IReadOnlyList<IMediaProfile> MapProfiles(GetProfiles2Response response)
        {
            return (response?.Profiles ?? Array.Empty<MediaProfile2>())
                .Where(p => !string.IsNullOrEmpty(p.Token))
                .Select(MediaProfile.CreateInstance)
                .ToList();
        }

        private static string RequireToken(string profileToken)
        {
            if (string.IsNullOrWhiteSpace(profileToken))
            {
                throw new ArgumentNullException(nameof(profileToken));
            }

            return profileToken;
        }

        private Media2Client CreateClient()
        {
            var client = new Media2Client(OnvifBindingFactory.CreateBinding(ServiceUri), new EndpointAddress(ServiceUri));
            var authenticator = PasswordDigestBuilder.GetBuilder().SetCamera(_camera).Build();
            client.Endpoint.EndpointBehaviors.Add(authenticator);
            authenticator.ConfigureCredentials(client.ClientCredentials);
            return client;
        }

        #endregion
    }
}
