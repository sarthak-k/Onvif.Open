// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Recording.Implementation.Archive
{
    /// <summary>
    /// How completed segments are moved to the archive store.
    /// </summary>
    public sealed class SegmentArchiveOptions
    {
        #region Public Variables

        /// <summary>Name shown in the storage information, e.g. "S3". Default: the store's type name.</summary>
        public string Name { get; set; }

        /// <summary>Prepended to every key, e.g. "site-a/". Default: none.</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>
        /// How long the local copy is kept after a successful upload (fast playback of recent recordings).
        /// Default: zero, the local disk is only a buffer.
        /// </summary>
        public TimeSpan KeepLocalCopyFor { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// When the local disk has less free space than this, local copies are removed oldest first, uploaded
        /// ones before ones that are not uploaded yet (recording must go on). Default: 1 GiB.
        /// </summary>
        public long MinLocalFreeBytes { get; set; } = 1024L * 1024 * 1024;

        /// <summary>Segments uploaded in parallel. Default: 2.</summary>
        public int MaxConcurrentUploads { get; set; } = 2;

        /// <summary>Back-off after a failed upload, doubling from minimum to maximum. Default: 1 s to 5 min.</summary>
        public TimeSpan MinRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// How often the archive is listed again, to notice objects removed by others (e.g. bucket lifecycle rules).
        /// Default: 15 minutes.
        /// </summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(15);

        #endregion

        #region Internal Methods

        /// <summary>A validated copy, with the prefix ending in '/'.</summary>
        internal SegmentArchiveOptions Normalize()
        {
            var copy = (SegmentArchiveOptions)MemberwiseClone();
            copy.Prefix = (Prefix ?? string.Empty).Trim('/');

            if (copy.Prefix.Length > 0)
            {
                if (!Storage.SegmentNames.IsSafeKey(copy.Prefix))
                {
                    throw new ArgumentException($"Archive prefix '{Prefix}' may only contain letters, digits, '-', '_', '.' and '/'.");
                }

                copy.Prefix += "/";
            }

            if (KeepLocalCopyFor < TimeSpan.Zero || MinLocalFreeBytes < 0 || MaxConcurrentUploads < 1 || MinRetryDelay <= TimeSpan.Zero || MaxRetryDelay < MinRetryDelay || RefreshInterval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(SegmentArchiveOptions), "Archive options are out of range.");
            }

            return copy;
        }

        #endregion
    }
}
