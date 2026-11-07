// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Recording
{
    /// <summary>
    /// State of the archive store that completed segments are uploaded to.
    /// </summary>
    public sealed class ArchiveInformation
    {
        #region Constructor

        public ArchiveInformation(string provider, string location, bool isAvailable, int pendingUploads, DateTimeOffset? lastUploadAt, string lastError, DateTimeOffset? lastErrorAt)
        {
            Provider = provider;
            Location = location;
            IsAvailable = isAvailable;
            PendingUploads = pendingUploads;
            LastUploadAt = lastUploadAt;
            LastError = lastError;
            LastErrorAt = lastErrorAt;
        }

        #endregion

        #region Public Variables

        /// <summary>Provider name, e.g. "S3".</summary>
        public string Provider { get; }

        /// <summary>Where the store is, e.g. "s3://bucket/prefix/".</summary>
        public string Location { get; }

        /// <summary>False until the store could be listed (uploads wait until then).</summary>
        public bool IsAvailable { get; }

        /// <summary>Completed local segments not uploaded yet.</summary>
        public int PendingUploads { get; }

        public DateTimeOffset? LastUploadAt { get; }
        public string LastError { get; }
        public DateTimeOffset? LastErrorAt { get; }

        #endregion
    }
}
