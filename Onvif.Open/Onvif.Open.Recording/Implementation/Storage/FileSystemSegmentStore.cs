// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;

namespace Onvif.Open.Recording.Implementation.Storage
{
    /// <summary>
    /// Built-in archive store for a directory: a NAS share (mounted path or UNC path), another disk, ...
    /// Uploads are written to a temporary file and then renamed, so a crash never leaves a half-written segment.
    /// </summary>
    public sealed class FileSystemSegmentStore : ISegmentStore
    {
        #region Constructor

        public FileSystemSegmentStore(string rootDirectory)
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

        private const string PartialSuffix = ".partial";
        private const int BufferSize = 256 * 1024;

        #endregion

        #region Public Variables

        public string RootDirectory { get; }

        #endregion

        #region Public Methods

        public async Task UploadAsync(string key, Stream content, CancellationToken cancellationToken)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var path = ToPath(key);
            var temporary = $"{path}.{Guid.NewGuid():N}{PartialSuffix}";
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            try
            {
                using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
                {
                    await content.CopyToAsync(target, BufferSize, cancellationToken).ConfigureAwait(false);
                    await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                    target.Flush(flushToDisk: true);
                }

                File.Move(temporary, path, overwrite: true);
            }
            catch
            {
                TryDelete(temporary);
                throw;
            }
        }

        public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            prefix = prefix ?? string.Empty;
            await Task.Yield();

            foreach (var file in Directory.EnumerateFiles(RootDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (file.EndsWith(PartialSuffix, StringComparison.Ordinal))
                {
                    continue;
                }

                var key = Path.GetRelativePath(RootDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                StoredObject stored;
                try
                {
                    var info = new FileInfo(file);
                    stored = new StoredObject(key, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
                }
                catch (IOException)
                {
                    continue;
                }

                yield return stored;
            }
        }

        public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken)
        {
            var path = ToPath(key);

            try
            {
                return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, useAsync: true));
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException)
            {
                return Task.FromResult<Stream>(null);
            }
        }

        public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken)
        {
            var path = ToPath(key);

            if (!File.Exists(path))
            {
                return Task.FromResult(false);
            }

            File.Delete(path);
            DeleteEmptyParents(Path.GetDirectoryName(path));
            return Task.FromResult(true);
        }

        public override string ToString()
        {
            return RootDirectory;
        }

        #endregion

        #region Private Methods

        private string ToPath(string key)
        {
            if (!SegmentNames.IsSafeKey(key))
            {
                throw new ArgumentException($"Invalid key '{key}'.", nameof(key));
            }

            var path = Path.GetFullPath(Path.Combine(RootDirectory, key.Replace('/', Path.DirectorySeparatorChar)));

            if (!path.StartsWith(RootDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new ArgumentException($"Key '{key}' is outside the store.", nameof(key));
            }

            return path;
        }

        private void DeleteEmptyParents(string directory)
        {
            try
            {
                while (directory != null && directory.Length > RootDirectory.Length && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                    directory = Path.GetDirectoryName(directory);
                }
            }
            catch (IOException)
            {
                // Another upload created a file in the folder meanwhile.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion
    }
}
