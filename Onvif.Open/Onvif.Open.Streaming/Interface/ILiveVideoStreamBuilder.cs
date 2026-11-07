// Sarthak Killedar
// 2026-11-07

using System;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Streaming.Implementation.Builder;

namespace Onvif.Open.Streaming.Interface
{
    internal interface ILiveVideoStreamBuilder
    {
        #region Public Methods

        Task<ILiveVideoStream> BuildAsync(CancellationToken cancellationToken = default);
        LiveVideoStreamBuilder SetStreamUri(Uri streamUri);
        LiveVideoStreamBuilder SetCamera(ICameraInformation camera);
        LiveVideoStreamBuilder SetCredentials(string username, string password);
        LiveVideoStreamBuilder SetTimeout(TimeSpan timeout);
        LiveVideoStreamBuilder SetFrameBufferCapacity(int capacity);

        #endregion
    }
}
