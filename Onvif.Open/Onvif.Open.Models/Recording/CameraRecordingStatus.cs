// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Core.Recording
{
    /// <summary>
    /// Snapshot of one camera's recorder.
    /// </summary>
    public sealed class CameraRecordingStatus
    {
        #region Public Variables

        public string CameraId { get; internal set; }
        public RecordingState State { get; internal set; }
        public RecordingMode Mode { get; internal set; }

        /// <summary>Triggered mode: whether a trigger (or its post-roll) is active, i.e. video is being written.</summary>
        public bool IsTriggered { get; internal set; }

        public DateTimeOffset? LastTriggeredAt { get; internal set; }
        public VideoCodec Codec { get; internal set; }
        public StreamTransport? Transport { get; internal set; }
        public Uri StreamUri { get; internal set; }

        /// <summary>File currently being written, if any.</summary>
        public string CurrentSegment { get; internal set; }

        public long FramesRecorded { get; internal set; }
        public long BytesRecorded { get; internal set; }
        public int SegmentsCompleted { get; internal set; }
        public DateTimeOffset? LastFrameAt { get; internal set; }
        public string LastError { get; internal set; }
        public DateTimeOffset? LastErrorAt { get; internal set; }

        #endregion

        #region Public Methods

        public CameraRecordingStatus Clone()
        {
            return (CameraRecordingStatus)MemberwiseClone();
        }

        #endregion
    }
}
