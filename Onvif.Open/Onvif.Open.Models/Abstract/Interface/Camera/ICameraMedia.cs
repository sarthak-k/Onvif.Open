// Sarthak Killedar
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Core.Abstract.Interface.Camera
{
    public interface ICameraMedia
    {
        IMediaService GetMediaService();

        /// <summary>
        /// Resolves the RTSP URI for <paramref name="profileToken"/> (or the first profile when null) and starts playing it.
        /// </summary>
        /// <param name="profileToken">Media profile to play; the first profile when null.</param>
        /// <param name="transport"><see cref="StreamTransport.Rtsp"/> (TCP, default) or <see cref="StreamTransport.Udp"/>.</param>
        /// <param name="cancellationToken">Cancels connecting.</param>
        Task<ILiveVideoStream> StartLiveStreamAsync(string profileToken = null, StreamTransport transport = StreamTransport.Rtsp, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a JPEG picture for <paramref name="profileToken"/> (or the first profile when null) from the device's snapshot URI.
        /// </summary>
        Task<byte[]> GetSnapshotAsync(string profileToken = null, CancellationToken cancellationToken = default);
    }
}
