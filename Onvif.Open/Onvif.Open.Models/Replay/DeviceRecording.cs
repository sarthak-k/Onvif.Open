// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;

namespace Onvif.Open.Core.Replay
{
    /// <summary>
    /// A recording stored on the device (SD card, NVR disk), found through the ONVIF Recording Search service.
    /// </summary>
    public sealed class DeviceRecording
    {
        #region Constructor

        public DeviceRecording(string token, string sourceName, string sourceId, string location, string content, DateTimeOffset? earliest, DateTimeOffset? latest, DeviceRecordingStatus status, IReadOnlyList<DeviceRecordingTrack> tracks)
        {
            Token = token;
            SourceName = sourceName;
            SourceId = sourceId;
            Location = location;
            Content = content;
            Earliest = earliest;
            Latest = latest;
            Status = status;
            Tracks = tracks ?? Array.Empty<DeviceRecordingTrack>();
        }

        #endregion

        #region Public Variables

        /// <summary>Recording token, used to replay it.</summary>
        public string Token { get; }

        /// <summary>Name of the recorded source, e.g. the camera or channel name.</summary>
        public string SourceName { get; }

        public string SourceId { get; }
        public string Location { get; }
        public string Content { get; }

        /// <summary>Start of the oldest recorded data, when known.</summary>
        public DateTimeOffset? Earliest { get; }

        /// <summary>End of the newest recorded data, when known.</summary>
        public DateTimeOffset? Latest { get; }

        public DeviceRecordingStatus Status { get; }
        public IReadOnlyList<DeviceRecordingTrack> Tracks { get; }

        #endregion
    }

    /// <summary>
    /// One track (video, audio or metadata) of a <see cref="DeviceRecording"/>.
    /// </summary>
    public sealed class DeviceRecordingTrack
    {
        #region Constructor

        public DeviceRecordingTrack(string token, DeviceTrackType type, string description, DateTimeOffset? from, DateTimeOffset? to)
        {
            Token = token;
            Type = type;
            Description = description;
            From = from;
            To = to;
        }

        #endregion

        #region Public Variables

        public string Token { get; }
        public DeviceTrackType Type { get; }
        public string Description { get; }
        public DateTimeOffset? From { get; }
        public DateTimeOffset? To { get; }

        #endregion
    }

    /// <summary>
    /// Time span and number of all recordings on the device.
    /// </summary>
    public sealed class DeviceRecordingSummary
    {
        #region Constructor

        public DeviceRecordingSummary(DateTimeOffset? dataFrom, DateTimeOffset? dataUntil, int recordingCount)
        {
            DataFrom = dataFrom;
            DataUntil = dataUntil;
            RecordingCount = recordingCount;
        }

        #endregion

        #region Public Variables

        public DateTimeOffset? DataFrom { get; }
        public DateTimeOffset? DataUntil { get; }
        public int RecordingCount { get; }

        #endregion
    }

    public enum DeviceTrackType
    {
        Unknown = 0,
        Video = 1,
        Audio = 2,
        Metadata = 3,
        Extended = 4
    }

    public enum DeviceRecordingStatus
    {
        Unknown = 0,
        Initiated = 1,
        Recording = 2,
        Stopped = 3,
        Removing = 4,
        Removed = 5
    }
}
