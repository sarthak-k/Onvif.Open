// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Storage
{
    /// <summary>
    /// An object in an <see cref="Abstract.Interface.Storage.ISegmentStore"/>.
    /// </summary>
    public sealed class StoredObject
    {
        #region Constructor

        public StoredObject(string key, long size, DateTimeOffset lastModified)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Size = size;
            LastModified = lastModified;
        }

        #endregion

        #region Public Variables

        public string Key { get; }
        public long Size { get; }
        public DateTimeOffset LastModified { get; }

        #endregion
    }
}
