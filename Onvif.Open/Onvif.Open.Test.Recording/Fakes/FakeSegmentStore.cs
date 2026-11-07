// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;

namespace Onvif.Open.Test.Recording.Fakes
{
    /// <summary>
    /// In-memory archive store that can be told to fail.
    /// </summary>
    internal sealed class FakeSegmentStore : ISegmentStore
    {
        public ConcurrentDictionary<string, byte[]> Objects { get; } = new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

        public volatile bool FailUploads;
        public volatile bool FailListing;
        private int _uploadAttempts;

        public int UploadAttempts => Volatile.Read(ref _uploadAttempts);

        public async Task UploadAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _uploadAttempts);

            if (FailUploads)
            {
                throw new IOException("store unavailable");
            }

            using (var buffer = new MemoryStream())
            {
                await content.CopyToAsync(buffer, cancellationToken);
                Objects[key] = buffer.ToArray();
            }
        }

        public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();

            if (FailListing)
            {
                throw new IOException("store unavailable");
            }

            foreach (var entry in Objects.Where(o => o.Key.StartsWith(prefix ?? string.Empty, StringComparison.Ordinal)).ToList())
            {
                yield return new StoredObject(entry.Key, entry.Value.Length, DateTimeOffset.UtcNow);
            }
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(Objects.TryGetValue(key, out var data) ? new MemoryStream(data, false) : null);
        }

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
        {
            return Task.FromResult(Objects.TryRemove(key, out _));
        }

        public override string ToString() => "memory://";
    }
}
