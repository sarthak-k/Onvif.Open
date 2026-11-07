// Sarthak Killedar
// 2026-11-07

using System;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Replay;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Replay.Ver10;

namespace Onvif.Open.Replay.Implementation.Client
{
    /// <summary>
    /// Client for the ONVIF Replay (ver10) service.
    /// </summary>
    public sealed class ReplayService : IReplayService
    {
        #region Constructor

        private ReplayService(Uri serviceUri, ICameraInformation camera)
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

        #endregion

        #region Public Methods

        public async Task<Uri> GetReplayUriAsync(string recordingToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(recordingToken))
            {
                throw new ArgumentNullException(nameof(recordingToken));
            }

            var client = new ReplayClient(OnvifBindingFactory.CreateBinding(ServiceUri), new EndpointAddress(ServiceUri));
            var request = new GetReplayUriRequest { RecordingToken = recordingToken };
            var response = await OnvifCall.InvokeAsync(client, client.Endpoint, client.ClientCredentials, _camera, () => client.Port.GetReplayUriAsync(request), cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response?.Uri) || !Uri.TryCreate(response.Uri.Trim(), UriKind.Absolute, out var uri))
            {
                throw new InvalidOperationException($"The device returned no valid replay URI for recording '{recordingToken}'.");
            }

            return uri;
        }

        internal static IReplayService CreateInstance(Uri serviceUri, ICameraInformation camera)
        {
            return new ReplayService(serviceUri, camera);
        }

        #endregion
    }
}
