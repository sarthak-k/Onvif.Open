// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Recording.Implementation.Storage
{
    /// <summary>
    /// Stores segments as <c>{root}/{cameraId}/{yyyy-MM-dd}/{cameraId}_{yyyyMMddTHHmmssfffZ}{extension}</c> (UTC).
    /// Existing recordings are picked up again after a restart because everything is derived from file names.
    /// </summary>
    public sealed class FileSystemRecordingStorage : IRecordingStorage
    {
        #region Constructor

        public FileSystemRecordingStorage(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentNullException(nameof(rootDirectory));
            }

            RootDirectory = Path.GetFullPath(rootDirectory);
            Directory.CreateDirectory(RootDirectory);
        }

        #endregion

        #region Private Variables

        private const int FileBufferSize = 256 * 1024;
        private const int MaxCreateAttempts = 5;

        private readonly ConcurrentDictionary<string, byte> _active = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

        #endregion

        #region Public Variables

        public string RootDirectory { get; }

        #endregion

        #region Public Methods

        public Stream CreateSegment(string cameraId, DateTimeOffset startTime, string fileExtension, out string path)
        {
            CameraIdentifier.Require(cameraId);

            var utc = startTime.ToUniversalTime();
            var directory = Path.Combine(RootDirectory, cameraId, SegmentNames.Day(utc));
            var baseName = SegmentNames.BaseName(cameraId, utc);
            FileStream stream = null;
            path = null;

            // Retention removes a day folder once its last file is deleted; if that happens between creating the
            // folder and creating the file, create the folder again rather than failing the recording.
            for (var attempt = 1; stream == null; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    path = Path.Combine(directory, baseName + fileExtension);

                    // Same millisecond (e.g. after a quick reconnect): add a suffix rather than overwrite.
                    for (var suffix = 1; File.Exists(path); suffix++)
                    {
                        path = Path.Combine(directory, $"{baseName}-{suffix}{fileExtension}");
                    }

                    stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, FileBufferSize);
                }
                catch (IOException) when (attempt < MaxCreateAttempts)
                {
                    // The folder vanished (or was being removed) underneath us, or another writer took the same
                    // name: try again with a fresh folder and name.
                }
            }

            _active[path] = 0;
            return new SegmentStream(stream, () => _active.TryRemove(stream.Name, out _));
        }

        public IReadOnlyList<RecordingSegment> GetSegments(string cameraId = null)
        {
            if (cameraId != null && !CameraIdentifier.IsValid(cameraId))
            {
                return Array.Empty<RecordingSegment>();
            }

            var cameraDirectories = cameraId == null
                ? Directory.EnumerateDirectories(RootDirectory)
                : new[] { Path.Combine(RootDirectory, cameraId) }.Where(Directory.Exists);

            var segments = new List<(RecordingSegment Segment, int Sequence)>();

            foreach (var cameraDirectory in cameraDirectories)
            {
                var id = Path.GetFileName(cameraDirectory);
                if (!CameraIdentifier.IsValid(id))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(cameraDirectory, id + "_*", SearchOption.AllDirectories))
                {
                    if (TryCreateSegment(id, file, out var segment, out var sequence))
                    {
                        segments.Add((segment, sequence));
                    }
                }
            }

            // Segments started within the same millisecond carry a "-n" suffix: order them by it.
            return segments
                .OrderBy(s => s.Segment.StartTime)
                .ThenBy(s => s.Segment.CameraId, StringComparer.Ordinal)
                .ThenBy(s => s.Sequence)
                .Select(s => s.Segment)
                .ToList();
        }

        public bool Delete(RecordingSegment segment)
        {
            if (segment == null)
            {
                throw new ArgumentNullException(nameof(segment));
            }

            var fullPath = Path.GetFullPath(segment.Path);

            // Only files inside this storage that are not being written may be deleted.
            if (!fullPath.StartsWith(RootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal) || _active.ContainsKey(fullPath))
            {
                return false;
            }

            try
            {
                File.Delete(fullPath);
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }

            DeleteEmptyDirectory(Path.GetDirectoryName(fullPath));
            return true;
        }

        public long? GetAvailableFreeBytes()
        {
            try
            {
                return new DriveInfo(RootDirectory).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public override string ToString()
        {
            return RootDirectory;
        }

        #endregion

        #region Private Methods

        private bool TryCreateSegment(string cameraId, string file, out RecordingSegment segment, out int sequence)
        {
            segment = null;

            if (!SegmentNames.TryParseFileName(cameraId, Path.GetFileName(file), out var start, out sequence))
            {
                return false;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(file);
                if (!info.Exists)
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return false;
            }

            segment = new RecordingSegment(cameraId, info.FullName, start, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero), info.Length, _active.ContainsKey(info.FullName));
            return true;
        }

        private void DeleteEmptyDirectory(string directory)
        {
            // Remove the day folder once its last segment is gone (never the camera or root folder).
            try
            {
                if (directory != null && Path.GetDirectoryName(Path.GetDirectoryName(directory)) == RootDirectory && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (IOException)
            {
            }
        }

        #endregion

        private sealed class SegmentStream : Stream
        {
            private readonly FileStream _inner;
            private readonly Action _onClosed;
            private bool _closed;

            public SegmentStream(FileStream inner, Action onClosed)
            {
                _inner = inner;
                _onClosed = onClosed;
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

            public override void Flush() => _inner.Flush();
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
            public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);

            protected override void Dispose(bool disposing)
            {
                if (disposing && !_closed)
                {
                    _closed = true;
                    _inner.Dispose();
                    _onClosed();
                }

                base.Dispose(disposing);
            }
        }
    }
}
