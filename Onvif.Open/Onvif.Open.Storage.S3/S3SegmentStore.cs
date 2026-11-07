// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;

namespace Onvif.Open.Storage.S3
{
    /// <summary>
    /// Segments as objects in an S3 bucket. Large segments are uploaded in parts.
    /// </summary>
    public sealed class S3SegmentStore : ISegmentStore, IDisposable
    {
        #region Constructor

        public S3SegmentStore(IAmazonS3 client, string bucket, S3StorageClass storageClass = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _bucket = string.IsNullOrWhiteSpace(bucket) ? throw new ArgumentNullException(nameof(bucket)) : bucket;
            _storageClass = storageClass;
            _transfer = new TransferUtility(client);
        }

        #endregion

        #region Private Variables

        private readonly IAmazonS3 _client;
        private readonly string _bucket;
        private readonly S3StorageClass _storageClass;
        private readonly TransferUtility _transfer;

        #endregion

        #region Public Methods

        public Task UploadAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            var request = new TransferUtilityUploadRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = content,
                AutoCloseStream = false,
                ContentType = "application/octet-stream"
            };

            if (_storageClass != null)
            {
                request.StorageClass = _storageClass;
            }

            return _transfer.UploadAsync(request, cancellationToken);
        }

        public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var request = new ListObjectsV2Request { BucketName = _bucket, Prefix = prefix ?? string.Empty };

            while (true)
            {
                var response = await _client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

                foreach (var item in response.S3Objects ?? new List<S3Object>())
                {
                    yield return new StoredObject(item.Key, item.Size ?? 0, item.LastModified.HasValue ? new DateTimeOffset(item.LastModified.Value.ToUniversalTime()) : DateTimeOffset.MinValue);
                }

                if (response.IsTruncated != true || string.IsNullOrEmpty(response.NextContinuationToken))
                {
                    yield break;
                }

                request.ContinuationToken = response.NextContinuationToken;
            }
        }

        public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _client.GetObjectAsync(_bucket, key, cancellationToken).ConfigureAwait(false);
                return response.ResponseStream;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
        {
            // S3 reports success for missing objects, so check first.
            try
            {
                await _client.GetObjectMetadataAsync(_bucket, key, cancellationToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            await _client.DeleteObjectAsync(_bucket, key, cancellationToken).ConfigureAwait(false);
            return true;
        }

        public override string ToString()
        {
            return $"s3://{_bucket}";
        }

        public void Dispose()
        {
            _transfer.Dispose();
            _client.Dispose();
        }

        #endregion
    }
}
