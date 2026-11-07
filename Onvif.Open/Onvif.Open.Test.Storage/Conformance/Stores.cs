// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using Google.Cloud.Storage.V1;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;
using Onvif.Open.Recording.Implementation.Storage;
using Onvif.Open.Storage.Azure;
using Onvif.Open.Storage.Gcs;
using Onvif.Open.Storage.S3;
using Xunit;

namespace Onvif.Open.Test.Storage.Conformance
{
    public sealed class FileSystemSegmentStoreTest : SegmentStoreConformance, IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "onvif-open-store-tests", Guid.NewGuid().ToString("N"));

        protected override Task<ISegmentStore> CreateStoreAsync()
        {
            return Task.FromResult<ISegmentStore>(new FileSystemSegmentStore(_root));
        }

        [Fact]
        public async Task KeysOutsideTheStoreShouldBeRejected()
        {
            var store = new FileSystemSegmentStore(_root);

            await Assert.ThrowsAsync<ArgumentException>(() => store.OpenReadAsync("../outside.ts", default));
            await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync("/etc/passwd", default));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }
    }

    public sealed class S3SegmentStoreTest : SegmentStoreConformance
    {
        protected override async Task<ISegmentStore> CreateStoreAsync()
        {
            var url = Environment.GetEnvironmentVariable("ONVIF_OPEN_TEST_S3_URL");
            Skip.If(string.IsNullOrWhiteSpace(url), "ONVIF_OPEN_TEST_S3_URL is not set (S3 emulator).");

            var bucket = "onvif-open-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var context = new SegmentStoreContext(new System.Collections.Generic.Dictionary<string, string>
            {
                ["Bucket"] = bucket,
                ["ServiceUrl"] = url,
                ["AccessKey"] = "test",
                ["SecretKey"] = "test"
            });

            var store = (S3SegmentStore)new S3SegmentStoreProvider().Create(context);

            using (var admin = new AmazonS3Client("test", "test", new AmazonS3Config { ServiceURL = url, ForcePathStyle = true, AuthenticationRegion = "us-east-1" }))
            {
                await admin.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
            }

            return store;
        }
    }

    public sealed class AzureBlobSegmentStoreTest : SegmentStoreConformance
    {
        protected override Task<ISegmentStore> CreateStoreAsync()
        {
            var connection = Environment.GetEnvironmentVariable("ONVIF_OPEN_TEST_AZURE_CONNECTION");
            Skip.If(string.IsNullOrWhiteSpace(connection), "ONVIF_OPEN_TEST_AZURE_CONNECTION is not set (Azurite).");

            // The provider creates the container.
            return Task.FromResult(new AzureBlobSegmentStoreProvider().Create(new SegmentStoreContext(new System.Collections.Generic.Dictionary<string, string>
            {
                ["ConnectionString"] = connection,
                ["Container"] = "onvif-open-" + Guid.NewGuid().ToString("N").Substring(0, 8)
            })));
        }
    }

    public sealed class GcsSegmentStoreTest : SegmentStoreConformance
    {
        protected override async Task<ISegmentStore> CreateStoreAsync()
        {
            var url = Environment.GetEnvironmentVariable("ONVIF_OPEN_TEST_GCS_URL");
            Skip.If(string.IsNullOrWhiteSpace(url), "ONVIF_OPEN_TEST_GCS_URL is not set (fake-gcs-server).");

            var bucket = "onvif-open-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var admin = await new StorageClientBuilder { BaseUri = url, UnauthenticatedAccess = true }.BuildAsync();
            await admin.CreateBucketAsync("test-project", bucket);

            return new GcsSegmentStoreProvider().Create(new SegmentStoreContext(new System.Collections.Generic.Dictionary<string, string>
            {
                ["Bucket"] = bucket,
                ["Endpoint"] = url
            }));
        }
    }
}
