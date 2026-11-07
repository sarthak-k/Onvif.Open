// Sarthak Killedar
// 2026-11-07

using System;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;

namespace Onvif.Open.Storage.Azure
{
    /// <summary>
    /// Azure Blob Storage (and Azurite for development).
    /// </summary>
    /// <remarks>
    /// Settings: <c>Container</c> (required, created when missing), and either <c>ConnectionString</c> or
    /// <c>AccountUrl</c> (e.g. https://account.blob.core.windows.net), which authenticates with
    /// DefaultAzureCredential (managed identity, environment, Azure CLI, ...). Optional <c>AccessTier</c>
    /// (Hot, Cool, Cold, Archive).
    /// </remarks>
    public sealed class AzureBlobSegmentStoreProvider : ISegmentStoreProvider
    {
        #region Public Variables

        public string Name => "AzureBlob";

        #endregion

        #region Public Methods

        public ISegmentStore Create(SegmentStoreContext context)
        {
            var containerName = context.Require("Container");
            var connectionString = context.Get("ConnectionString");
            var accountUrl = context.Get("AccountUrl");

            BlobServiceClient service;

            if (connectionString != null)
            {
                service = new BlobServiceClient(connectionString);
            }
            else if (accountUrl != null)
            {
                service = new BlobServiceClient(new Uri(accountUrl), new DefaultAzureCredential());
            }
            else
            {
                throw new InvalidOperationException("Storage setting 'ConnectionString' or 'AccountUrl' is required.");
            }

            var tier = context.Get("AccessTier");
            return new AzureBlobSegmentStore(service.GetBlobContainerClient(containerName), tier == null ? (AccessTier?)null : new AccessTier(tier));
        }

        #endregion
    }
}
