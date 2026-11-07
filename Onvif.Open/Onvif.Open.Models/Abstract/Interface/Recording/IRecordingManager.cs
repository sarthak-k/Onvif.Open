// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Core.Abstract.Interface.Recording
{
    /// <summary>
    /// Records many cameras to storage: one segment file after another per camera, reconnecting
    /// automatically and applying the retention policy.
    /// </summary>
    public interface IRecordingManager : IAsyncDisposable
    {
        #region Public Variables

        /// <summary>Raised after a segment file has been closed.</summary>
        event EventHandler<RecordingSegment> SegmentCompleted;

        #endregion

        #region Public Methods

        /// <summary>Records an ONVIF camera (stream URI resolved through the media service).</summary>
        void AddCamera(string cameraId, ICamera camera, string profileToken = null, StreamTransport transport = StreamTransport.Rtsp, IRotationPolicy rotationPolicy = null);

        /// <summary>Records any stream source, e.g. a plain RTSP URI opened with LiveVideoStreamBuilder.</summary>
        void AddCamera(string cameraId, Func<CancellationToken, Task<ILiveVideoStream>> streamFactory, IRotationPolicy rotationPolicy = null);

        Task<bool> RemoveCameraAsync(string cameraId, CancellationToken cancellationToken = default);

        Task StartAsync(CancellationToken cancellationToken = default);

        Task StopAsync(CancellationToken cancellationToken = default);

        Task StartCameraAsync(string cameraId, CancellationToken cancellationToken = default);

        Task StopCameraAsync(string cameraId, CancellationToken cancellationToken = default);

        IReadOnlyList<CameraRecordingStatus> GetStatus();

        /// <summary>Segments on storage, oldest first; all cameras when <paramref name="cameraId"/> is null.</summary>
        IReadOnlyList<RecordingSegment> GetSegments(string cameraId = null);

        /// <summary>Deletes a completed segment. Returns false when it does not exist or is still being recorded.</summary>
        bool DeleteSegment(RecordingSegment segment);

        /// <summary>Location and free space of the storage.</summary>
        StorageInformation GetStorageInformation();

        /// <summary>Applies the retention policy now (it also runs periodically and after each segment).</summary>
        Task<IReadOnlyList<RecordingSegment>> ApplyRetentionAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
