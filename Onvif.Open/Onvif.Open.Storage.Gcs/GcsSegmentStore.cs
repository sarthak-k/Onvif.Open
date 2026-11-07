// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Google;
using Google.Cloud.Storage.V1;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;

namespace Onvif.Open.Storage.Gcs
{
    /// <summary>
    /// Segments as objects in a Google Cloud Storage bucket. Uploads are resumable.
    /// </summary>
    public sealed class GcsSegmentStore : ISegmentStore, IDisposable
    {
        #region Constructor

        public GcsSegmentStore(StorageClient client, string bucket, string storageClass = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _bucket = string.IsNullOrWhiteSpace(bucket) ? throw new ArgumentNullException(nameof(bucket)) : bucket;
            _storageClass = storageClass;
        }

        #endregion

        #region Private Variables

        private const int BufferSize = 256 * 1024;

        private readonly StorageClient _client;
        private readonly string _bucket;
        private readonly string _storageClass;

        #endregion

        #region Public Methods

        public Task UploadAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            var destination = new Google.Apis.Storage.v1.Data.Object
            {
                Bucket = _bucket,
                Name = key,
                ContentType = "application/octet-stream",
                StorageClass = _storageClass
            };

            return _client.UploadObjectAsync(destination, content, cancellationToken: cancellationToken);
        }

        public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in _client.ListObjectsAsync(_bucket, string.IsNullOrEmpty(prefix) ? null : prefix).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return new StoredObject(item.Name, (long)(item.Size ?? 0), item.UpdatedDateTimeOffset ?? DateTimeOffset.MinValue);
            }
        }

        public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
        {
            // Downloaded to a temporary file so the caller gets a seekable stream (byte ranges for playback).
            var path = Path.Combine(Path.GetTempPath(), "onvif-open-" + Guid.NewGuid().ToString("N"));
            var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            try
            {
                await _client.DownloadObjectAsync(_bucket, key, file, cancellationToken: cancellationToken).ConfigureAwait(false);
                file.Position = 0;
                return file;
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                await file.DisposeAsync().ConfigureAwait(false);
                return null;
            }
            catch
            {
                await file.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
        {
            try
            {
                await _client.DeleteObjectAsync(_bucket, key, cancellationToken: cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public override string ToString()
        {
            return $"gs://{_bucket}";
        }

        public void Dispose()
        {
            _client.Dispose();
        }

        #endregion
    }
}
