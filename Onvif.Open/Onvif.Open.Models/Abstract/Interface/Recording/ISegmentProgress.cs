// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Abstract.Interface.Recording
{
    /// <summary>
    /// The segment currently being written, as seen by an <see cref="IRotationPolicy"/>.
    /// </summary>
    public interface ISegmentProgress
    {
        #region Public Variables

        string CameraId { get; }

        /// <summary>Wall-clock time the segment was started.</summary>
        DateTimeOffset StartedAt { get; }

        /// <summary>Wall-clock time now.</summary>
        DateTimeOffset Now { get; }

        /// <summary>Recorded media duration (from frame timestamps) up to the frame about to be written.</summary>
        TimeSpan Duration { get; }

        /// <summary>Bytes written so far.</summary>
        long Size { get; }

        int FrameCount { get; }

        #endregion
    }
}
