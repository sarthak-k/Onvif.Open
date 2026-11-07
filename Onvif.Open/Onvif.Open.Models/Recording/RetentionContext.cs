// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;

namespace Onvif.Open.Core.Recording
{
    /// <summary>
    /// Input of an <see cref="Abstract.Interface.Recording.IRetentionPolicy"/>.
    /// </summary>
    public sealed class RetentionContext
    {
        #region Constructor

        public RetentionContext(IReadOnlyList<RecordingSegment> segments, DateTimeOffset now, long? availableFreeBytes)
        {
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
            Now = now;
            AvailableFreeBytes = availableFreeBytes;
        }

        #endregion

        #region Public Variables

        /// <summary>Completed segments of every camera, oldest first.</summary>
        public IReadOnlyList<RecordingSegment> Segments { get; }

        public DateTimeOffset Now { get; }

        /// <summary>Free space of the storage volume, when known.</summary>
        public long? AvailableFreeBytes { get; }

        #endregion
    }
}
