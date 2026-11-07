// Sarthak Killedar
// 2026-11-07

namespace Onvif.Open.Core.Recording
{
    public sealed class StorageInformation
    {
        #region Constructor

        public StorageInformation(string location, long? availableFreeBytes, ArchiveInformation archive = null)
        {
            Archive = archive;
            Location = location;
            AvailableFreeBytes = availableFreeBytes;
        }

        #endregion

        #region Public Variables

        public string Location { get; }
        public long? AvailableFreeBytes { get; }

        /// <summary>The archive store, or null when recordings are only kept locally.</summary>
        public ArchiveInformation Archive { get; }

        #endregion
    }
}
