// Sarthak Killedar
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Core.Abstract.Interface.Media
{
    /// <summary>
    /// ONVIF Media service (ver10) operations required to discover and open live video.
    /// </summary>
    public interface IMediaService
    {
        Uri ServiceUri { get; }

        IReadOnlyList<IMediaProfile> GetProfiles();
        Task<IReadOnlyList<IMediaProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);

        Uri GetStreamUri(string profileToken, StreamTransport transport = StreamTransport.Rtsp);
        Task<Uri> GetStreamUriAsync(string profileToken, StreamTransport transport = StreamTransport.Rtsp, CancellationToken cancellationToken = default);

        Uri GetSnapshotUri(string profileToken);
        Task<Uri> GetSnapshotUriAsync(string profileToken, CancellationToken cancellationToken = default);
    }
}
