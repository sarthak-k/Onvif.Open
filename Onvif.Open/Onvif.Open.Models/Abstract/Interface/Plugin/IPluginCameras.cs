// Sarthak Killedar
// 2026-11-07

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Access to the cameras assigned to a plugin. Using any other camera throws <see cref="System.UnauthorizedAccessException"/>.
    /// </summary>
    public interface IPluginCameras
    {
        #region Public Methods

        bool IsAssigned(string cameraId);

        IReadOnlyList<CameraRecordingStatus> GetStatus();

        IReadOnlyList<RecordingSegment> GetSegments(string cameraId);

        Task StartRecordingAsync(string cameraId, CancellationToken cancellationToken = default);

        Task StopRecordingAsync(string cameraId, CancellationToken cancellationToken = default);

        /// <summary>
        /// A JPEG picture from the camera (ONVIF GetSnapshotUri).
        /// Throws <see cref="System.NotSupportedException"/> for cameras without ONVIF snapshots.
        /// </summary>
        Task<byte[]> GetSnapshotAsync(string cameraId, CancellationToken cancellationToken = default);

        #endregion
    }
}
