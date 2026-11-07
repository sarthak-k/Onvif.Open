# Recording storage

Recordings can stay on the recorder's disk, or be archived to the storage the customer picks:

| Where | Provider | Package |
| --- | --- | --- |
| Local disk only | (none) | built in |
| NAS share, another disk | `FileSystem` | built in |
| Amazon S3, MinIO, Wasabi, Backblaze B2, Cloudflare R2, Ceph, ... | `Onvif.Open.Storage.S3` | plugin |
| Azure Blob Storage | `Onvif.Open.Storage.Azure` | plugin |
| Google Cloud Storage | `Onvif.Open.Storage.Gcs` | plugin |
| Anything else | your own `ISegmentStore` | plugin |

## How it works

```
camera ──► local disk (StorageDirectory) ──upload──► archive store (cloud / NAS)
            write buffer                              long-term storage, retention
```

* Recording **always writes to the local disk first**. A slow, unreachable or misconfigured store never interrupts or
  slows down a recording.
* Each completed segment is uploaded. Failed uploads are retried with back-off (1 s up to 5 min). Nothing is kept in
  memory only: segments that were not uploaded before a restart are found on the disk and uploaded then.
* Once uploaded, the local copy is removed. Set `KeepLocalCopyFor` to keep it longer, e.g. for fast playback of the
  last day.
* **Retention** (`MaxAge`, `MaxTotalSizeGB`, `MaxSizePerCameraGB`) applies to the archive. `MinFreeSpaceGB` protects the
  local disk. When the disk runs short, local copies are removed oldest first: uploaded ones first, and as a last resort
  ones that could not be uploaded yet, because recording must go on. A warning is logged for each of those.
* The API and plugins see both places as one: `GET /api/cameras/{id}/segments` lists local and archived segments
  (`isLocal`, `isArchived`), downloads are served from wherever the segment is, deletes remove it everywhere.
  `GET /api/storage` shows the archive state (`pendingUploads`, `lastError`).
* Objects are stored as `{Prefix}/{cameraId}/{yyyy-MM-dd}/{cameraId}_{yyyyMMddTHHmmssfffZ}.ts`. Only the recorder writes
  there, and nothing else needs to be persisted: everything is derived from the names.

## Configuration

`Recording:Archive` in `appsettings.json`:

| Setting | Default | |
| --- | --- | --- |
| `Provider` | empty (no archive) | `FileSystem`, or the folder name of a storage plugin in `Plugins:Directory` |
| `Prefix` | empty | Key prefix, e.g. `site-a` when several recorders share a bucket |
| `KeepLocalCopyFor` | `00:00:00` | Keep the local copy this long after upload |
| `MaxConcurrentUploads` | `2` | |
| `RefreshInterval` | `00:15:00` | How often the archive is listed again (notices objects removed by lifecycle rules) |
| `Settings` | | Provider settings, see below |

Keep secrets out of `appsettings.json`: prefer the cloud's own identity (IAM role, managed identity, workload identity)
or pass keys as environment variables, e.g. `Recording__Archive__Settings__SecretKey`.

### NAS / another disk (`FileSystem`, built in)

```json
"Archive": { "Provider": "FileSystem", "Settings": { "Path": "/mnt/nas/recordings" } }
```

`Path` can be a mounted share (NFS, SMB/CIFS) or a UNC path on Windows (`\\nas\recordings`). Files are written to a
temporary name and then renamed, so a crash never leaves a half-written segment behind.

### Amazon S3 and S3-compatible storage (`Onvif.Open.Storage.S3`)

```json
"Archive": {
  "Provider": "Onvif.Open.Storage.S3",
  "Settings": { "Bucket": "my-recordings", "Region": "eu-west-1", "StorageClass": "STANDARD_IA" }
}
```

| Setting | |
| --- | --- |
| `Bucket` | Required |
| `Region` | e.g. `eu-west-1` |
| `ServiceUrl` | S3-compatible endpoint, e.g. `https://s3.wasabisys.com`, `http://minio:9000` |
| `ForcePathStyle` | Default `true` with `ServiceUrl` |
| `StorageClass` | e.g. `STANDARD_IA`, `INTELLIGENT_TIERING` |
| `AccessKey`, `SecretKey`, `SessionToken` | Optional: without them the AWS credential chain is used (environment, profile, IAM role) |

Segments larger than 16 MB are uploaded in parts. The recorder needs `s3:PutObject`, `s3:GetObject`, `s3:DeleteObject`
and `s3:ListBucket` on the bucket.

### Azure Blob Storage (`Onvif.Open.Storage.Azure`)

```json
"Archive": {
  "Provider": "Onvif.Open.Storage.Azure",
  "Settings": { "AccountUrl": "https://myaccount.blob.core.windows.net", "Container": "recordings", "AccessTier": "Cool" }
}
```

| Setting | |
| --- | --- |
| `Container` | Required; created when missing |
| `AccountUrl` | Authenticates with `DefaultAzureCredential` (managed identity, environment, Azure CLI, ...) |
| `ConnectionString` | Alternative to `AccountUrl` |
| `AccessTier` | `Hot`, `Cool`, `Cold` or `Archive` |

The identity needs the *Storage Blob Data Contributor* role.

### Google Cloud Storage (`Onvif.Open.Storage.Gcs`)

```json
"Archive": {
  "Provider": "Onvif.Open.Storage.Gcs",
  "Settings": { "Bucket": "my-recordings", "StorageClass": "NEARLINE" }
}
```

| Setting | |
| --- | --- |
| `Bucket` | Required |
| `CredentialsFile` | Service account key file; default: Application Default Credentials (workload identity, `GOOGLE_APPLICATION_CREDENTIALS`, ...) |
| `StorageClass` | e.g. `NEARLINE`, `COLDLINE` |
| `Endpoint` | Emulators only |

The identity needs the *Storage Object User* role on the bucket. GCS can also be used through its S3-compatible XML API
with the S3 provider and HMAC keys (`ServiceUrl`: `https://storage.googleapis.com`).

## Deploying a storage plugin

Storage plugins are deployed like other plugins: publish the project and copy the output into a folder named after the
assembly in the plugin directory.

```
dotnet publish Onvif.Open/Onvif.Open.Storage.S3 -c Release -o plugins/Onvif.Open.Storage.S3
```

```
plugins/
  Onvif.Open.Storage.S3/
    Onvif.Open.Storage.S3.dll
    AWSSDK.S3.dll
    ...
```

Each plugin is loaded into its own load context, so its SDK and dependencies never conflict with the recorder or other
plugins. The recorder fails at startup with a clear message when the folder or a required setting is missing.

## Writing a storage plugin

Implement two small interfaces from `Onvif.Open.Core`. The recorder does all the bookkeeping (naming, retries,
retention, the local buffer), so a store only stores, lists, reads and deletes objects:

```csharp
public sealed class MyStoreProvider : ISegmentStoreProvider
{
    public string Name => "MyStore";

    public ISegmentStore Create(SegmentStoreContext context)
    {
        return new MyStore(context.Require("Endpoint"), context.Get("Prefix", "recordings"));
    }
}

public sealed class MyStore : ISegmentStore
{
    public Task UploadAsync(string key, Stream content, CancellationToken cancellationToken) { ... }       // replace if it exists
    public IAsyncEnumerable<StoredObject> ListAsync(string prefix, CancellationToken cancellationToken) { ... }
    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken) { ... }             // null when missing
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken) { ... }                 // false when missing
}
```

* `UploadAsync` gets a seekable stream with a known length; when it returns, the object must be durable.
* Methods may be called concurrently. Implement `IDisposable` or `IAsyncDisposable` to release clients.
* Reference `Onvif.Open.Core` with `Private=false` and `ExcludeAssets=runtime` and set `EnableDynamicLoading` (see any
  `Onvif.Open.Storage.*` project).
* Check your store against the shared conformance tests: derive a class from `SegmentStoreConformance` in
  `Onvif.Open.Test.Storage`, as the built-in stores do.

Without the recorder service, pass any store to the library directly:
`RecordingManagerBuilder.GetBuilder().SetStorageDirectory(...).SetArchive(store, new SegmentArchiveOptions { ... })`.
