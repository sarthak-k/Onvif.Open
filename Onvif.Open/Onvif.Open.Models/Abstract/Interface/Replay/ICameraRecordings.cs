// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Replay;

namespace Onvif.Open.Core.Abstract.Interface.Replay
{
    /// <summary>
    /// Recordings stored on the camera itself (SD card, NVR disk): ONVIF Profile G search and replay.
    /// Throws <see cref="NotSupportedException"/> when the device has no Recording Search or Replay service.
    /// </summary>
    public interface ICameraRecordings
    {
        #region Public Methods

        /// <summary>Time span and number of the stored recordings.</summary>
        Task<DeviceRecordingSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<DeviceRecording>> GetRecordingsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Plays <paramref name="recordingToken"/> from <paramref name="from"/>: read the frames like live video
        /// (<see cref="ILiveVideoStream.ReadFramesAsync"/>, <see cref="ILiveVideoStream.CopyToAsync"/>);
        /// each frame carries its recording time in <see cref="Streaming.VideoFrame.CaptureTime"/>.
        /// The stream ends at <see cref="ReplayOptions.To"/> or at the end of the recording.
        /// </summary>
        Task<ILiveVideoStream> StartReplayAsync(string recordingToken, DateTimeOffset from, ReplayOptions options = null, CancellationToken cancellationToken = default);

        #endregion
    }
}
