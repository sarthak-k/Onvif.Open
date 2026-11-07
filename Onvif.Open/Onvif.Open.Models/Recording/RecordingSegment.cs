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

        public RecordingSegment(string cameraId, string path, DateTimeOffset startTime, DateTimeOffset lastWriteTime, long size, bool isActive)
        {
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
        public string Path { get; }
        public string FileName => System.IO.Path.GetFileName(Path);
        public DateTimeOffset StartTime { get; }
        public DateTimeOffset LastWriteTime { get; }
        public long Size { get; }

        /// <summary>True while the recorder is still writing the file.</summary>
        public bool IsActive { get; }

        #endregion
    }
}
