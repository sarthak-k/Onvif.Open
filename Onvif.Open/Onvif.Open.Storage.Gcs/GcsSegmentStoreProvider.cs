// Sarthak Killedar
// 2026-11-07

using Google.Apis.Auth.OAuth2;
using Google.Cloud.Storage.V1;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;

namespace Onvif.Open.Storage.Gcs
{
    /// <summary>
    /// Google Cloud Storage.
    /// </summary>
    /// <remarks>
    /// Settings: <c>Bucket</c> (required), <c>CredentialsFile</c> (service account JSON; default: Application
    /// Default Credentials, e.g. workload identity or GOOGLE_APPLICATION_CREDENTIALS), <c>StorageClass</c>
    /// (e.g. NEARLINE), <c>Endpoint</c> (emulators only, e.g. http://localhost:4443/storage/v1/).
    /// </remarks>
    public sealed class GcsSegmentStoreProvider : ISegmentStoreProvider
    {
        #region Public Variables

        public string Name => "GCS";

        #endregion

        #region Public Methods

        public ISegmentStore Create(SegmentStoreContext context)
        {
            var bucket = context.Require("Bucket");
            var endpoint = context.Get("Endpoint");
            var credentialsFile = context.Get("CredentialsFile");

            var builder = new StorageClientBuilder();

            if (endpoint != null)
            {
                builder.BaseUri = endpoint;
                builder.UnauthenticatedAccess = credentialsFile == null;
            }

            if (credentialsFile != null)
            {
                // Only service account keys are accepted from a file (never arbitrary credential configurations).
                builder.GoogleCredential = CredentialFactory.FromFile<ServiceAccountCredential>(credentialsFile).ToGoogleCredential();
            }

            return new GcsSegmentStore(builder.Build(), bucket, context.Get("StorageClass"));
        }

        #endregion
    }
}
