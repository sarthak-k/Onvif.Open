// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recording.Implementation.Archive;
using Onvif.Open.Recording.Implementation.Builder;
using Onvif.Open.Recording.Implementation.Policy;
using Onvif.Open.Recording.Implementation.Storage;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    public class SegmentArchiveTest
    {
        #region Public Methods

        [Fact]
        public async Task CompletedSegmentsShouldBeUploadedAndRemovedLocally()
        {
            using (var temp = new TempDirectory())
            using (var nas = new TempDirectory())
            {
                var store = new FileSystemSegmentStore(nas.Path);

                await using (var manager = Builder(temp, store, new SegmentArchiveOptions { Prefix = "site-a" }).Build())
                {
                    manager.AddCamera("cam1", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(15, gop: 5)).ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => manager.GetSegments("cam1").Count(s => s.IsArchived && !s.IsLocal) == 2, "two segments are archived and removed locally");

                    var segments = manager.GetSegments("cam1");
                    Assert.Equal(3, segments.Count);
                    Assert.True(segments.Last().IsActive && segments.Last().IsLocal && !segments.Last().IsArchived);

                    // Stored under {prefix}/{camera}/{day}/{file}.
                    var archived = segments[0];
                    var expected = Path.Combine(nas.Path, "site-a", "cam1", archived.StartTime.UtcDateTime.ToString("yyyy-MM-dd"), archived.FileName);
                    Assert.True(File.Exists(expected), expected);
                    Assert.Single(Directory.GetFiles(Path.Combine(temp.Path, "cam1"), "*", SearchOption.AllDirectories));

                    // Readable through the manager even though there is no local copy.
                    using (var stream = await manager.OpenSegmentAsync(archived))
                    using (var copy = new MemoryStream())
                    {
                        await stream.CopyToAsync(copy);
                        Assert.Equal(archived.Size, copy.Length);
                        Assert.True(copy.Length > 0 && copy.Length % 188 == 0);
                    }

                    var information = manager.GetStorageInformation().Archive;
                    Assert.True(information.IsAvailable);
                    Assert.Equal(0, information.PendingUploads);
                    Assert.NotNull(information.LastUploadAt);
                    Assert.Contains(nas.Path, information.Location);
                }
            }
        }

        [Fact]
        public async Task LocalCopiesShouldBeKeptForTheConfiguredTime()
        {
            using (var temp = new TempDirectory())
            {
                var store = new FakeSegmentStore();

                await using (var manager = Builder(temp, store, new SegmentArchiveOptions { KeepLocalCopyFor = TimeSpan.FromHours(1) }).Build())
                {
                    manager.AddCamera("cam1", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(10, gop: 5)).ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => store.Objects.Count == 1, "the completed segment is uploaded");
                    await manager.ApplyRetentionAsync();

                    var archived = manager.GetSegments("cam1").First();
                    Assert.True(archived.IsLocal && archived.IsArchived);
                    Assert.True(File.Exists(archived.Path));
                }
            }
        }

        [Fact]
        public async Task FailedUploadsShouldBeRetriedAndKeepTheLocalCopy()
        {
            using (var temp = new TempDirectory())
            {
                var store = new FakeSegmentStore { FailUploads = true };

                await using (var manager = Builder(temp, store).Build())
                {
                    manager.AddCamera("cam1", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(10, gop: 5)).ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => store.UploadAttempts >= 3, "the upload is retried");
                    await manager.ApplyRetentionAsync();

                    var information = manager.GetStorageInformation().Archive;
                    Assert.Equal(1, information.PendingUploads);
                    Assert.Contains("store unavailable", information.LastError);
                    Assert.True(manager.GetSegments("cam1").First().IsLocal);

                    store.FailUploads = false;
                    await Eventually.True(() => manager.GetSegments("cam1").First() is var s && s.IsArchived && !s.IsLocal, "the upload succeeds and the local copy is removed");
                    Assert.Equal(0, manager.GetStorageInformation().Archive.PendingUploads);
                }
            }
        }

        [Fact]
        public async Task SegmentsRecordedBeforeShouldBeUploadedWhenTheArchiveBecomesAvailable()
        {
            using (var temp = new TempDirectory())
            {
                // Recorded without an archive (or before a restart).
                await using (var manager = Builder(temp).Build())
                {
                    manager.AddCamera("cam1", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(15, gop: 5)).ConnectAsync);
                    await manager.StartAsync();
                    await Eventually.True(() => manager.GetSegments("cam1").Count == 3, "three segments are recorded");
                }

                var store = new FakeSegmentStore { FailListing = true };

                await using (var manager = Builder(temp, store).Build())
                {
                    await manager.StartAsync();

                    // Nothing is uploaded or removed while the store cannot be listed.
                    await Eventually.True(() => manager.GetStorageInformation().Archive.LastError != null, "listing fails");
                    Assert.False(manager.GetStorageInformation().Archive.IsAvailable);
                    await manager.ApplyRetentionAsync();
                    Assert.Equal(3, manager.GetSegments("cam1").Count(s => s.IsLocal));

                    store.FailListing = false;
                    await Eventually.True(() => store.Objects.Count == 3 && manager.GetSegments("cam1").All(s => !s.IsLocal), "all segments are archived");
                }
            }
        }

        [Fact]
        public async Task RetentionShouldApplyToTheArchive()
        {
            using (var temp = new TempDirectory())
            {
                var store = new FakeSegmentStore();
                var oldestFirst = RetentionPolicies.When(c => c.Segments.Count > 2 ? c.Segments.Take(c.Segments.Count - 2) : Enumerable.Empty<RecordingSegment>());

                await using (var manager = Builder(temp, store).SetRetentionPolicy(oldestFirst).Build())
                {
                    manager.AddCamera("cam1", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(25, gop: 5)).ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => manager.GetSegments("cam1").Count(s => s.IsArchived) == 2 && store.Objects.Count == 2 && manager.GetStatus()[0].SegmentsCompleted == 4, "only the two newest archived segments are kept");
                    await manager.ApplyRetentionAsync();

                    var archived = manager.GetSegments("cam1").Where(s => s.IsArchived).ToList();
                    Assert.Equal(2, archived.Count);
                    Assert.Equal(archived.Select(s => s.FileName).OrderBy(n => n), store.Objects.Keys.Select(Path.GetFileName).OrderBy(n => n));
                }
            }
        }

        [Fact]
        public async Task DeleteShouldRemoveTheSegmentEverywhere()
        {
            using (var temp = new TempDirectory())
            {
                var store = new FakeSegmentStore();

                await using (var manager = Builder(temp, store, new SegmentArchiveOptions { KeepLocalCopyFor = TimeSpan.FromHours(1) }).Build())
                {
                    manager.AddCamera("cam1", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(10, gop: 5)).ConnectAsync);
                    await manager.StartAsync();
                    await Eventually.True(() => manager.GetSegments("cam1").FirstOrDefault()?.IsArchived == true, "the segment is archived");

                    var segment = manager.GetSegments("cam1").First();
                    Assert.True(await manager.DeleteSegmentAsync(segment));

                    Assert.Empty(store.Objects);
                    Assert.False(File.Exists(segment.Path));
                    Assert.DoesNotContain(manager.GetSegments("cam1"), s => s.FileName == segment.FileName);
                    Assert.False(await manager.DeleteSegmentAsync(segment));

                    // The active segment cannot be deleted.
                    Assert.False(await manager.DeleteSegmentAsync(manager.GetSegments("cam1").Last()));
                }
            }
        }

        [Fact]
        public async Task LowDiskSpaceShouldRemoveLocalSegmentsEvenWhenNotUploaded()
        {
            using (var temp = new TempDirectory())
            {
                var storage = new LowSpaceStorage(new FileSystemRecordingStorage(temp.Path));
                var store = new FakeSegmentStore { FailUploads = true };

                await using (var manager = RecordingManagerBuilder.GetBuilder()
                    .SetStorage(storage)
                    .SetRetentionPolicy(RetentionPolicies.KeepAll)
                    .SetRotationPolicy(RotationPolicies.When(s => s.FrameCount >= 5))
                    .SetArchive(store, FastOptions(new SegmentArchiveOptions { MinLocalFreeBytes = 1000 }))
                    .Build())
                {
                    manager.AddCamera("cam1", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(15, gop: 5)).ConnectAsync);
                    await manager.StartAsync();
                    await Eventually.True(() => manager.GetStatus()[0].SegmentsCompleted == 2, "two segments are completed");

                    await manager.ApplyRetentionAsync();
                    Assert.Equal(3, manager.GetSegments("cam1").Count);

                    // Space is short: the oldest segment goes, even though it was never uploaded.
                    storage.FreeBytes = 1000 - manager.GetSegments("cam1").First().Size;
                    var deleted = await manager.ApplyRetentionAsync();

                    Assert.Single(deleted);
                    Assert.Equal(2, manager.GetSegments("cam1").Count);
                    Assert.Equal(1, manager.GetStorageInformation().Archive.PendingUploads);
                }
            }
        }

        [Fact]
        public async Task OpenSegmentShouldOnlyOpenKnownSegments()
        {
            using (var temp = new TempDirectory())
            {
                var outside = Path.Combine(temp.Path, "secret.txt");
                File.WriteAllText(outside, "secret");

                await using (var manager = Builder(temp, new FakeSegmentStore()).Build())
                {
                    var forged = new RecordingSegment("cam1", outside, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 6, false);
                    Assert.Null(await manager.OpenSegmentAsync(forged));
                }
            }
        }

        #endregion

        #region Private Methods

        private static RecordingManagerBuilder Builder(TempDirectory temp, Onvif.Open.Core.Abstract.Interface.Storage.ISegmentStore store = null, SegmentArchiveOptions options = null)
        {
            var builder = RecordingManagerBuilder.GetBuilder()
                .SetStorageDirectory(temp.Path)
                .SetRetentionPolicy(RetentionPolicies.KeepAll)
                .SetRotationPolicy(RotationPolicies.When(s => s.FrameCount >= 5))
                .SetReconnectDelay(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50))
                .SetStallTimeout(TimeSpan.FromSeconds(5));

            return store == null ? builder : builder.SetArchive(store, FastOptions(options ?? new SegmentArchiveOptions()));
        }

        private static SegmentArchiveOptions FastOptions(SegmentArchiveOptions options)
        {
            options.MinRetryDelay = TimeSpan.FromMilliseconds(20);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(100);
            options.MinLocalFreeBytes = options.MinLocalFreeBytes == new SegmentArchiveOptions().MinLocalFreeBytes ? 0 : options.MinLocalFreeBytes;
            return options;
        }

        #endregion

        /// <summary>Local storage reporting a chosen amount of free space.</summary>
        private sealed class LowSpaceStorage : IRecordingStorage
        {
            private readonly IRecordingStorage _inner;

            public LowSpaceStorage(IRecordingStorage inner)
            {
                _inner = inner;
            }

            public long FreeBytes { get; set; } = long.MaxValue;

            public Stream CreateSegment(string cameraId, DateTimeOffset startTime, string fileExtension, out string path) => _inner.CreateSegment(cameraId, startTime, fileExtension, out path);
            public IReadOnlyList<RecordingSegment> GetSegments(string cameraId = null) => _inner.GetSegments(cameraId);
            public bool Delete(RecordingSegment segment) => _inner.Delete(segment);
            public long? GetAvailableFreeBytes() => FreeBytes;
        }
    }
}
