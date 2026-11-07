// Sarthak Killedar
// 2026-11-07

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Storage;

namespace Onvif.Open.Core.Abstract.Interface.Storage
{
    /// <summary>
    /// Long-term storage for completed recordings: a cloud bucket, a NAS share, another disk, ...
    /// Recording always writes to the local disk first; completed segments are then uploaded here,
    /// so a slow or unreachable store never interrupts a recording.
    /// </summary>
    /// <remarks>
    /// Keys look like <c>{prefix}{cameraId}/{yyyy-MM-dd}/{fileName}</c> and only contain letters, digits,
    /// '-', '_', '.' and '/'. The host does all bookkeeping (naming, retention, retries): an implementation
    /// only stores, lists, reads and deletes objects. Methods may be called concurrently. Implement
    /// <see cref="System.IAsyncDisposable"/> or <see cref="System.IDisposable"/> to release clients.
    /// </remarks>
    public interface ISegmentStore
    {
        #region Public Methods

        /// <summary>
        /// Stores <paramref name="content"/> under <paramref name="key"/>, replacing an existing object.
        /// The content is seekable and its length is known. When this returns, the object must be durable.
        /// </summary>
        Task UploadAsync(string key, Stream content, CancellationToken cancellationToken);

        /// <summary>Objects whose key starts with <paramref name="prefix"/> (empty: all), in any order.</summary>
        IAsyncEnumerable<StoredObject> ListAsync(string prefix, CancellationToken cancellationToken);

        /// <summary>Opens an object for reading, or returns null when it does not exist.</summary>
        Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken);

        /// <summary>Deletes an object. Returns false when it did not exist.</summary>
        Task<bool> DeleteAsync(string key, CancellationToken cancellationToken);

        #endregion
    }
}
