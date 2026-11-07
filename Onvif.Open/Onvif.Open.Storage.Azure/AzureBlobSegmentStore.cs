// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;

namespace Onvif.Open.Storage.Azure
{
    /// <summary>
    /// Segments as block blobs in one container. Reads are seekable, so the recorder serves byte ranges.
    /// </summary>
    public sealed class AzureBlobSegmentStore : ISegmentStore
    {
        #region Constructor

        public AzureBlobSegmentStore(BlobContainerClient container, AccessTier? accessTier = null)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _accessTier = accessTier;
        }

        #endregion

        #region Private Variables

        private readonly BlobContainerClient _container;
        private readonly AccessTier? _accessTier;
        private readonly SemaphoreSlim _createLock = new SemaphoreSlim(1, 1);
        private volatile bool _created;

        #endregion

        #region Public Methods

        public async Task UploadAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            await EnsureContainerAsync(cancellationToken).ConfigureAwait(false);

            var options = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = "application/octet-stream" },
                AccessTier = _accessTier
            };

            await _container.GetBlobClient(key).UploadAsync(content, options, cancellationToken).ConfigureAwait(false);
        }

        public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await EnsureContainerAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var blob in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, string.IsNullOrEmpty(prefix) ? null : prefix, cancellationToken).ConfigureAwait(false))
            {
                yield return new StoredObject(blob.Name, blob.Properties.ContentLength ?? 0, blob.Properties.LastModified ?? DateTimeOffset.MinValue);
            }
        }

        public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
        {
            try
            {
                return await _container.GetBlobClient(key).OpenReadAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                return response.Value;
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // The container itself is gone.
                return false;
            }
        }

        public override string ToString()
        {
            return _container.Uri.ToString();
        }

        #endregion

        #region Private Methods

        private async Task EnsureContainerAsync(CancellationToken cancellationToken)
        {
            if (_created)
            {
                return;
            }

            await _createLock.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (!_created)
                {
                    await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                    _created = true;
                }
            }
            finally
            {
                _createLock.Release();
            }
        }

        #endregion
    }
}
