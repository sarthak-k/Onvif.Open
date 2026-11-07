// Sarthak Killedar
// 2026-11-07

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;

namespace Onvif.Open.Storage.S3
{
    /// <summary>
    /// Amazon S3 and S3-compatible storage (MinIO, Wasabi, Backblaze B2, Ceph, Cloudflare R2, ...).
    /// </summary>
    /// <remarks>
    /// Settings: <c>Bucket</c> (required), <c>Region</c>, <c>ServiceUrl</c> (S3-compatible endpoint),
    /// <c>ForcePathStyle</c> (default true with ServiceUrl), <c>StorageClass</c> (e.g. STANDARD_IA),
    /// <c>AccessKey</c> / <c>SecretKey</c> / <c>SessionToken</c>. Without keys the standard AWS credential chain
    /// is used (environment, profile, IAM role), which is preferable to keys in configuration.
    /// </remarks>
    public sealed class S3SegmentStoreProvider : ISegmentStoreProvider
    {
        #region Public Variables

        public string Name => "S3";

        #endregion

        #region Public Methods

        public ISegmentStore Create(SegmentStoreContext context)
        {
            var bucket = context.Require("Bucket");
            var region = context.Get("Region");
            var serviceUrl = context.Get("ServiceUrl");
            var config = new AmazonS3Config();

            if (serviceUrl != null)
            {
                config.ServiceURL = serviceUrl;
                config.AuthenticationRegion = region ?? "us-east-1";
                config.ForcePathStyle = context.GetBoolean("ForcePathStyle", true);

                // Not every S3-compatible service supports the newer default checksums.
                config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
                config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
            }
            else if (region != null)
            {
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
            }

            var accessKey = context.Get("AccessKey");
            AmazonS3Client client;

            if (accessKey == null)
            {
                client = new AmazonS3Client(config);
            }
            else
            {
                var secretKey = context.Require("SecretKey");
                var sessionToken = context.Get("SessionToken");
                AWSCredentials credentials = sessionToken == null
                    ? new BasicAWSCredentials(accessKey, secretKey)
                    : new SessionAWSCredentials(accessKey, secretKey, sessionToken);
                client = new AmazonS3Client(credentials, config);
            }

            var storageClass = context.Get("StorageClass");
            return new S3SegmentStore(client, bucket, storageClass == null ? null : S3StorageClass.FindValue(storageClass));
        }

        #endregion
    }
}
