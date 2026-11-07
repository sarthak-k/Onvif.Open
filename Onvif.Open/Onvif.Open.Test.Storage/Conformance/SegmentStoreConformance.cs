// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;
using Xunit;

namespace Onvif.Open.Test.Storage.Conformance
{
    /// <summary>
    /// What every <see cref="ISegmentStore"/> must do. Each test works under its own random prefix,
    /// so the suites can share one emulator.
    /// </summary>
    public abstract class SegmentStoreConformance : IAsyncLifetime
    {
        #region Private Variables

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

        private readonly string _prefix = "t" + Guid.NewGuid().ToString("N").Substring(0, 12) + "/";
        private ISegmentStore _store;

        #endregion

        #region Protected Methods

        /// <summary>Creates the store; call Skip.If(...) when its backend is not configured.</summary>
        protected abstract Task<ISegmentStore> CreateStoreAsync();

        #endregion

        #region Public Methods

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            switch (_store)
            {
                case IAsyncDisposable asyncDisposable:
                    await asyncDisposable.DisposeAsync();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        [SkippableFact]
        public async Task UploadedObjectsShouldBeListedReadAndDeleted()
        {
            var store = await Store();
            var key = _prefix + "cam1/2026-11-07/cam1_20261107T101500000Z.ts";
            var content = Bytes(188 * 100, 1);

            using (var timeout = new CancellationTokenSource(Timeout))
            {
                await store.UploadAsync(key, new MemoryStream(content), timeout.Token);

                var listed = await List(store, _prefix, timeout.Token);
                var stored = Assert.Single(listed);
                Assert.Equal(key, stored.Key);
                Assert.Equal(content.Length, stored.Size);
                Assert.True(DateTimeOffset.UtcNow - stored.LastModified < TimeSpan.FromMinutes(10), $"LastModified {stored.LastModified}");

                Assert.Equal(content, await Read(store, key, timeout.Token));

                Assert.True(await store.DeleteAsync(key, timeout.Token));
                Assert.Empty(await List(store, _prefix, timeout.Token));
            }
        }

        [SkippableFact]
        public async Task UploadShouldReplaceAnExistingObject()
        {
            var store = await Store();
            var key = _prefix + "cam1/2026-11-07/cam1_20261107T101500000Z-1.ts";

            using (var timeout = new CancellationTokenSource(Timeout))
            {
                await store.UploadAsync(key, new MemoryStream(Bytes(1000, 1)), timeout.Token);
                await store.UploadAsync(key, new MemoryStream(Bytes(500, 2)), timeout.Token);

                Assert.Equal(Bytes(500, 2), await Read(store, key, timeout.Token));
                Assert.Equal(500, Assert.Single(await List(store, _prefix, timeout.Token)).Size);
            }
        }

        [SkippableFact]
        public async Task ListShouldOnlyReturnObjectsWithThePrefix()
        {
            var store = await Store();

            using (var timeout = new CancellationTokenSource(Timeout))
            {
                await Task.WhenAll(
                    store.UploadAsync(_prefix + "cam1/2026-11-07/a.ts", new MemoryStream(Bytes(10, 1)), timeout.Token),
                    store.UploadAsync(_prefix + "cam1/2026-11-08/b.ts", new MemoryStream(Bytes(20, 2)), timeout.Token),
                    store.UploadAsync(_prefix + "cam2/2026-11-07/c.ts", new MemoryStream(Bytes(30, 3)), timeout.Token));

                Assert.Equal(new[] { "cam1/2026-11-07/a.ts", "cam1/2026-11-08/b.ts", "cam2/2026-11-07/c.ts" }, Keys(await List(store, _prefix, timeout.Token)));
                Assert.Equal(new[] { "cam1/2026-11-07/a.ts", "cam1/2026-11-08/b.ts" }, Keys(await List(store, _prefix + "cam1/", timeout.Token)));
                Assert.Empty(await List(store, _prefix + "cam3/", timeout.Token));
            }
        }

        [SkippableFact]
        public async Task MissingObjectsShouldNotThrow()
        {
            var store = await Store();

            using (var timeout = new CancellationTokenSource(Timeout))
            {
                Assert.Null(await store.OpenReadAsync(_prefix + "missing.ts", timeout.Token));
                Assert.False(await store.DeleteAsync(_prefix + "missing.ts", timeout.Token));
            }
        }

        [SkippableFact]
        public async Task LargeObjectsShouldRoundTrip()
        {
            // Above the multipart / chunked upload thresholds of the cloud SDKs.
            var store = await Store();
            var key = _prefix + "cam1/2026-11-07/large.ts";
            var content = Bytes(24 * 1024 * 1024 + 123, 7);

            using (var timeout = new CancellationTokenSource(Timeout))
            {
                await store.UploadAsync(key, new MemoryStream(content), timeout.Token);

                Assert.Equal(content.Length, Assert.Single(await List(store, _prefix, timeout.Token)).Size);
                var read = await Read(store, key, timeout.Token);
                Assert.True(content.AsSpan().SequenceEqual(read), "content differs");
            }
        }

        #endregion

        #region Private Methods

        private async Task<ISegmentStore> Store()
        {
            return _store ?? (_store = await CreateStoreAsync());
        }

        private static async Task<List<StoredObject>> List(ISegmentStore store, string prefix, CancellationToken cancellationToken)
        {
            var result = new List<StoredObject>();

            await foreach (var item in store.ListAsync(prefix, cancellationToken))
            {
                result.Add(item);
            }

            return result;
        }

        private IEnumerable<string> Keys(IEnumerable<StoredObject> objects)
        {
            return objects.Select(o => o.Key.Substring(_prefix.Length)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        private static async Task<byte[]> Read(ISegmentStore store, string key, CancellationToken cancellationToken)
        {
            using (var stream = await store.OpenReadAsync(key, cancellationToken))
            using (var copy = new MemoryStream())
            {
                Assert.NotNull(stream);
                await stream.CopyToAsync(copy, cancellationToken);
                return copy.ToArray();
            }
        }

        private static byte[] Bytes(int length, int seed)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }

        #endregion
    }
}
