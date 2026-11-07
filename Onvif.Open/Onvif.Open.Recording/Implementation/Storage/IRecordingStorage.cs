// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Recording.Implementation.Storage
{
    /// <summary>
    /// Where segments are stored. <see cref="FileSystemRecordingStorage"/> is the built-in implementation.
    /// </summary>
    public interface IRecordingStorage
    {
        #region Public Methods

        /// <summary>Creates a new segment; the segment is reported as active until the returned stream is disposed.</summary>
        Stream CreateSegment(string cameraId, DateTimeOffset startTime, string fileExtension, out string path);

        /// <summary>Segments, oldest first; all cameras when <paramref name="cameraId"/> is null.</summary>
        IReadOnlyList<RecordingSegment> GetSegments(string cameraId = null);

        /// <summary>Deletes a completed segment. Returns false when it no longer exists or is still being written.</summary>
        bool Delete(RecordingSegment segment);

        /// <summary>Free space of the storage volume, or null when unknown.</summary>
        long? GetAvailableFreeBytes();

        #endregion
    }
}
