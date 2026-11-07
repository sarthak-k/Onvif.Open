// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Plugin;
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

        /// <summary>
        /// Raised for every recorded frame, on the camera's recording thread: handlers must return quickly.
        /// </summary>
        event EventHandler<CameraFrame> FrameReceived;

        /// <summary>Raised when a camera's recorder changes state.</summary>
        event EventHandler<CameraStateChange> CameraStateChanged;

        /// <summary>Raised for every ONVIF event received from a camera (cameras with event subscriptions only).</summary>
        event EventHandler<CameraEvent> CameraEventReceived;

        #endregion

        #region Public Methods

        /// <summary>Records an ONVIF camera (stream URI resolved through the media service).</summary>
        void AddCamera(string cameraId, ICamera camera, string profileToken = null, StreamTransport transport = StreamTransport.Rtsp, IRotationPolicy rotationPolicy = null);

        /// <summary>Records an ONVIF camera with full control over mode, triggers and stream.</summary>
        void AddCamera(string cameraId, ICamera camera, CameraRecordingOptions options);

        /// <summary>Records any stream source, e.g. a plain RTSP URI opened with LiveVideoStreamBuilder.</summary>
        void AddCamera(string cameraId, Func<CancellationToken, Task<ILiveVideoStream>> streamFactory, IRotationPolicy rotationPolicy = null);

        /// <summary>
        /// Records any stream source; <paramref name="eventSource"/> optionally supplies camera events
        /// (used for <see cref="CameraRecordingOptions.EventTriggers"/> and <see cref="CameraEventReceived"/>).
        /// </summary>
        void AddCamera(string cameraId, Func<CancellationToken, Task<ILiveVideoStream>> streamFactory, CameraRecordingOptions options, Func<CancellationToken, IAsyncEnumerable<CameraEvent>> eventSource = null);

        /// <summary>
        /// Triggered mode: records for at least <paramref name="duration"/> from now (plus the post-roll).
        /// Has no effect on continuously recorded cameras.
        /// </summary>
        void TriggerRecording(string cameraId, TimeSpan duration);

        Task<bool> RemoveCameraAsync(string cameraId, CancellationToken cancellationToken = default);

        Task StartAsync(CancellationToken cancellationToken = default);

        Task StopAsync(CancellationToken cancellationToken = default);

        Task StartCameraAsync(string cameraId, CancellationToken cancellationToken = default);

        Task StopCameraAsync(string cameraId, CancellationToken cancellationToken = default);

        IReadOnlyList<CameraRecordingStatus> GetStatus();

        /// <summary>
        /// Segments on local storage and in the archive, oldest first; all cameras when <paramref name="cameraId"/> is null.
        /// </summary>
        IReadOnlyList<RecordingSegment> GetSegments(string cameraId = null);

        /// <summary>Opens a segment for reading, from the local storage or the archive. Returns null when it no longer exists.</summary>
        Task<System.IO.Stream> OpenSegmentAsync(RecordingSegment segment, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes a completed segment locally and from the archive. Returns false when it does not exist or is still being recorded.
        /// </summary>
        Task<bool> DeleteSegmentAsync(RecordingSegment segment, CancellationToken cancellationToken = default);

        /// <summary>Location and free space of the storage, and the state of the archive.</summary>
        StorageInformation GetStorageInformation();

        /// <summary>Applies the retention policy now (it also runs periodically and after each segment).</summary>
        Task<IReadOnlyList<RecordingSegment>> ApplyRetentionAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
