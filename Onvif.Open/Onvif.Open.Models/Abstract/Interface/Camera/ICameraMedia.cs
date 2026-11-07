// Sarthak Killedar
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Core.Abstract.Interface.Streaming;

namespace Onvif.Open.Core.Abstract.Interface.Camera
{
    public interface ICameraMedia
    {
        IMediaService GetMediaService();

        /// <summary>
        /// Resolves the RTSP URI for <paramref name="profileToken"/> (or the first profile when null) and starts playing it.
        /// </summary>
        Task<ILiveVideoStream> StartLiveStreamAsync(string profileToken = null, CancellationToken cancellationToken = default);
    }
}
