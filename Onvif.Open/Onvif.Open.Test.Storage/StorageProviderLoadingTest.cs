// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using Onvif.Open.Core.Storage;
using Onvif.Open.Plugins.Implementation.Loading;
using Xunit;

namespace Onvif.Open.Test.Storage
{
    public class StorageProviderLoadingTest
    {
        [Theory]
        [InlineData("Onvif.Open.Storage.S3", "S3", "s3://recordings")]
        [InlineData("Onvif.Open.Storage.Azure", "AzureBlob", "/recordings")]
        [InlineData("Onvif.Open.Storage.Gcs", "GCS", "gs://recordings")]
        public void ProvidersShouldLoadFromADeployedFolder(string folderName, string name, string location)
        {
            var root = Path.Combine(Path.GetTempPath(), "onvif-open-storage-plugins", Guid.NewGuid().ToString("N"));
            var folder = Path.Combine(root, folderName);
            Directory.CreateDirectory(folder);

            try
            {
                // Like a deployment: the provider's build output in a folder named after its assembly.
                var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
                var output = Path.Combine(testOutput.Parent.Parent.Parent.Parent.FullName, folderName, "bin", testOutput.Parent.Name, testOutput.Name);

                foreach (var file in Directory.GetFiles(output))
                {
                    File.Copy(file, Path.Combine(folder, Path.GetFileName(file)));
                }

                var provider = PluginLoader.LoadStorageProvider(folder);
                Assert.Equal(name, provider.Name);

                // Creating a store does not contact the service.
                var store = provider.Create(new SegmentStoreContext(new Dictionary<string, string>
                {
                    ["Bucket"] = "recordings",
                    ["Container"] = "recordings",
                    ["ConnectionString"] = "UseDevelopmentStorage=true",
                    ["Region"] = "eu-west-1",
                    ["Endpoint"] = "http://127.0.0.1:1/storage/v1/"
                }));

                Assert.EndsWith(location, store.ToString());
                (store as IDisposable)?.Dispose();
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
        }

        [Fact]
        public void MissingSettingsShouldBeReported()
        {
            var error = Assert.Throws<InvalidOperationException>(() => new Onvif.Open.Storage.S3.S3SegmentStoreProvider().Create(new SegmentStoreContext(null)));
            Assert.Contains("Bucket", error.Message);

            error = Assert.Throws<InvalidOperationException>(() => new Onvif.Open.Storage.Azure.AzureBlobSegmentStoreProvider().Create(new SegmentStoreContext(new Dictionary<string, string> { ["Container"] = "c" })));
            Assert.Contains("ConnectionString", error.Message);
        }
    }
}
