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

        /// <summary>Opens a segment for reading, wherever it is stored (local disk or archive); null when it no longer exists.</summary>
        Task<System.IO.Stream> OpenSegmentAsync(RecordingSegment segment, CancellationToken cancellationToken = default);

        Task StartRecordingAsync(string cameraId, CancellationToken cancellationToken = default);

        Task StopRecordingAsync(string cameraId, CancellationToken cancellationToken = default);

        /// <summary>
        /// For cameras in triggered mode: record for at least <paramref name="duration"/> from now (plus the
        /// camera's post-roll), e.g. when the plugin detected something. Ignored for continuous recording.
        /// </summary>
        void TriggerRecording(string cameraId, System.TimeSpan duration);

        /// <summary>
        /// A JPEG picture from the camera (ONVIF GetSnapshotUri).
        /// Throws <see cref="System.NotSupportedException"/> for cameras without ONVIF snapshots.
        /// </summary>
        Task<byte[]> GetSnapshotAsync(string cameraId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Pan/tilt/zoom control of the camera, e.g. to turn to a preset when something happens.
        /// Throws <see cref="System.NotSupportedException"/> for cameras without ONVIF PTZ.
        /// </summary>
        Ptz.ICameraPtz GetPtz(string cameraId);

        /// <summary>
        /// Image settings (brightness, day/night, ...) and focus of the camera.
        /// Throws <see cref="System.NotSupportedException"/> for cameras without ONVIF Imaging.
        /// </summary>
        Imaging.ICameraImaging GetImaging(string cameraId);

        /// <summary>
        /// Relay outputs and digital inputs of the camera, e.g. to sound a siren on a detection.
        /// Throws <see cref="System.NotSupportedException"/> for cameras without ONVIF.
        /// </summary>
        DeviceIo.ICameraIo GetIo(string cameraId);

        /// <summary>
        /// Recordings stored on the camera itself (SD card, ONVIF Profile G), e.g. to fetch footage from before the
        /// recorder saw an incident. Throws <see cref="System.NotSupportedException"/> for cameras without ONVIF.
        /// </summary>
        Replay.ICameraRecordings GetDeviceRecordings(string cameraId);

        #endregion
    }
}
