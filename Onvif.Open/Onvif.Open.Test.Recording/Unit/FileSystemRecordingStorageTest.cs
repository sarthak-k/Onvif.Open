// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Linq;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recording.Implementation.Storage;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class FileSystemRecordingStorageTest
    {
        #region Private Variables

        private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 7, 10, 15, 30, 123, TimeSpan.Zero);

        #endregion

        #region Public Methods

        [Fact]
        public void CreateSegmentShouldUseCameraAndDateLayout()
        {
            using (var temp = new TempDirectory())
            {
                var storage = new FileSystemRecordingStorage(temp.Path);

                using (storage.CreateSegment("front-door", Start, ".ts", out var path))
                {
                    Assert.Equal(Path.Combine(temp.Path, "front-door", "2026-11-07", "front-door_20261107T101530123Z.ts"), path);
                }
            }
        }

        [Fact]
        public void SegmentShouldBeActiveUntilStreamIsDisposed()
        {
            using (var temp = new TempDirectory())
            {
                var storage = new FileSystemRecordingStorage(temp.Path);
                var stream = storage.CreateSegment("cam1", Start, ".ts", out _);
                stream.Write(new byte[10], 0, 10);
                stream.Flush();

                var active = Assert.Single(storage.GetSegments());
                Assert.True(active.IsActive);
                Assert.False(storage.Delete(active));

                stream.Dispose();

                var completed = Assert.Single(storage.GetSegments("cam1"));
                Assert.False(completed.IsActive);
                Assert.Equal(10, completed.Size);
                Assert.Equal(Start, completed.StartTime);
                Assert.Equal("cam1", completed.CameraId);
            }
        }

        [Fact]
        public void GetSegmentsShouldBeOldestFirstAndIgnoreForeignFiles()
        {
            using (var temp = new TempDirectory())
            {
                var storage = new FileSystemRecordingStorage(temp.Path);
                storage.CreateSegment("cam1", Start.AddDays(1), ".ts", out _).Dispose();
                storage.CreateSegment("cam2", Start, ".ts", out _).Dispose();
                storage.CreateSegment("cam1", Start.AddHours(-1), ".ts", out _).Dispose();
                File.WriteAllText(Path.Combine(temp.Path, "cam1", "notes.txt"), "x");
                File.WriteAllText(Path.Combine(temp.Path, "cam1", "cam1_garbage.ts"), "x");

                var all = storage.GetSegments();
                Assert.Equal(new[] { Start.AddHours(-1), Start, Start.AddDays(1) }, all.Select(s => s.StartTime));
                Assert.Equal(2, storage.GetSegments("cam1").Count);
                Assert.Empty(storage.GetSegments("unknown"));
                Assert.Empty(storage.GetSegments("../etc"));
            }
        }

        [Fact]
        public void SameStartTimeShouldNotOverwrite()
        {
            using (var temp = new TempDirectory())
            {
                var storage = new FileSystemRecordingStorage(temp.Path);
                storage.CreateSegment("cam1", Start, ".ts", out var first).Dispose();
                storage.CreateSegment("cam1", Start, ".ts", out var second).Dispose();

                storage.CreateSegment("cam1", Start, ".ts", out var third).Dispose();

                Assert.NotEqual(first, second);
                Assert.Equal(new[] { first, second, third }, storage.GetSegments("cam1").Select(s => s.Path));
            }
        }

        [Fact]
        public void DeleteShouldRemoveFileAndEmptyDayFolder()
        {
            using (var temp = new TempDirectory())
            {
                var storage = new FileSystemRecordingStorage(temp.Path);
                storage.CreateSegment("cam1", Start, ".ts", out var path).Dispose();
                var segment = Assert.Single(storage.GetSegments());

                Assert.True(storage.Delete(segment));
                Assert.False(File.Exists(path));
                Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
                Assert.True(Directory.Exists(Path.Combine(temp.Path, "cam1")));
                Assert.False(storage.Delete(segment));
            }
        }

        [Fact]
        public void DeleteShouldRefuseFilesOutsideStorage()
        {
            using (var temp = new TempDirectory())
            using (var other = new TempDirectory())
            {
                var outside = Path.Combine(other.Path, "keep.ts");
                File.WriteAllText(outside, "x");
                var storage = new FileSystemRecordingStorage(temp.Path);

                Assert.False(storage.Delete(new RecordingSegment("cam1", outside, Start, Start, 1, false)));
                Assert.False(storage.Delete(new RecordingSegment("cam1", Path.Combine(temp.Path, "..", Path.GetFileName(other.Path), "keep.ts"), Start, Start, 1, false)));
                Assert.True(File.Exists(outside));
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData("../x")]
        [InlineData("a/b")]
        [InlineData("-cam")]
        [InlineData("cam 1")]
        public void InvalidCameraIdShouldThrow(string cameraId)
        {
            using (var temp = new TempDirectory())
            {
                var storage = new FileSystemRecordingStorage(temp.Path);
                Assert.Throws<ArgumentException>(() => storage.CreateSegment(cameraId, Start, ".ts", out _));
            }
        }

        [Fact]
        public void FreeSpaceShouldBeReported()
        {
            using (var temp = new TempDirectory())
            {
                Assert.True(new FileSystemRecordingStorage(temp.Path).GetAvailableFreeBytes() > 0);
            }
        }

        #endregion
    }
}
