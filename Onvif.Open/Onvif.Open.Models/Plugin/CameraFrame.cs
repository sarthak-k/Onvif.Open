// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Core.Plugin
{
    /// <summary>
    /// A video frame of one camera. The frame data is immutable and may be kept after the callback returns.
    /// </summary>
    public sealed class CameraFrame
    {
        #region Constructor

        public CameraFrame(string cameraId, VideoFrame frame, DateTimeOffset receivedAt, bool isRecorded = true)
        {
            IsRecorded = isRecorded;
            CameraId = cameraId;
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            ReceivedAt = receivedAt;
        }

        #endregion

        #region Public Variables

        public string CameraId { get; }

        /// <summary>Encoded frame (H.264/H.265 Annex-B access unit).</summary>
        public VideoFrame Frame { get; }

        /// <summary>Wall-clock time the frame was received.</summary>
        public DateTimeOffset ReceivedAt { get; }

        /// <summary>
        /// True when the frame was written into a recording; false e.g. for a triggered camera between triggers
        /// (frames are still delivered then, so analytics can decide to trigger).
        /// </summary>
        public bool IsRecorded { get; }

        #endregion
    }
}
