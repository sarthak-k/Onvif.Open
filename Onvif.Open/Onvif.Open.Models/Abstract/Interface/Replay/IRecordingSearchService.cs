// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Replay;

namespace Onvif.Open.Core.Abstract.Interface.Replay
{
    /// <summary>
    /// ONVIF Recording Search service (Profile G): which recordings the device stores.
    /// </summary>
    public interface IRecordingSearchService
    {
        #region Public Variables

        Uri ServiceUri { get; }

        #endregion

        #region Public Methods

        Task<DeviceRecordingSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

        /// <summary>All recordings on the device (runs a search session and collects every result).</summary>
        Task<IReadOnlyList<DeviceRecording>> FindRecordingsAsync(CancellationToken cancellationToken = default);

        /// <summary>One recording, or null when the token is unknown.</summary>
        Task<DeviceRecording> GetRecordingAsync(string recordingToken, CancellationToken cancellationToken = default);

        #endregion
    }

    /// <summary>
    /// ONVIF Replay service (Profile G): the RTSP URI that plays a stored recording.
    /// </summary>
    public interface IReplayService
    {
        #region Public Variables

        Uri ServiceUri { get; }

        #endregion

        #region Public Methods

        Task<Uri> GetReplayUriAsync(string recordingToken, CancellationToken cancellationToken = default);

        #endregion
    }
}
