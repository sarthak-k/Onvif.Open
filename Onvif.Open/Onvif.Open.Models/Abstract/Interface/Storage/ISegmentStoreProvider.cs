// Sarthak Killedar
// 2026-11-07

using Onvif.Open.Core.Storage;

namespace Onvif.Open.Core.Abstract.Interface.Storage
{
    /// <summary>
    /// Entry point of a storage plugin: creates the <see cref="ISegmentStore"/> from its settings.
    /// Storage plugins are built and deployed like other plugins (one folder per assembly, see docs/STORAGE.md);
    /// the public type implementing this interface needs a parameterless constructor.
    /// </summary>
    public interface ISegmentStoreProvider
    {
        #region Public Variables

        /// <summary>Short name for logs and the storage API, e.g. "S3".</summary>
        string Name { get; }

        #endregion

        #region Public Methods

        /// <summary>Validates the settings and creates the store. Throw for missing or invalid settings.</summary>
        ISegmentStore Create(SegmentStoreContext context);

        #endregion
    }
}
