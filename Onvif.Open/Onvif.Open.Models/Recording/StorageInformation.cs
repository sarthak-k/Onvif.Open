// Sarthak Killedar
// 2026-11-07

namespace Onvif.Open.Core.Recording
{
    public sealed class StorageInformation
    {
        #region Constructor

        public StorageInformation(string location, long? availableFreeBytes)
        {
            Location = location;
            AvailableFreeBytes = availableFreeBytes;
        }

        #endregion

        #region Public Variables

        public string Location { get; }
        public long? AvailableFreeBytes { get; }

        #endregion
    }
}
