// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Recording
{
    /// <summary>
    /// One recorded file.
    /// </summary>
    public sealed class RecordingSegment
    {
        #region Constructor

        public RecordingSegment(string cameraId, string path, DateTimeOffset startTime, DateTimeOffset lastWriteTime, long size, bool isActive, bool isLocal = true, bool isArchived = false)
        {
            IsLocal = isLocal;
            IsArchived = isArchived;
            CameraId = cameraId;
            Path = path;
            StartTime = startTime;
            LastWriteTime = lastWriteTime;
            Size = size;
            IsActive = isActive;
        }

        #endregion

        #region Public Variables

        public string CameraId { get; }

        /// <summary>
        /// The local file; for a segment that is only in the archive, its archive key.
        /// Use <see cref="Abstract.Interface.Recording.IRecordingManager.OpenSegmentAsync"/> to read either.
        /// </summary>
        public string Path { get; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public DateTimeOffset StartTime { get; }
        public DateTimeOffset LastWriteTime { get; }
        public long Size { get; }

        /// <summary>True while the recorder is still writing the file.</summary>
        public bool IsActive { get; }

        /// <summary>True when the file is on the local recording storage.</summary>
        public bool IsLocal { get; }

        /// <summary>True when the segment has been uploaded to the archive store.</summary>
        public bool IsArchived { get; }

        #endregion
    }
}
