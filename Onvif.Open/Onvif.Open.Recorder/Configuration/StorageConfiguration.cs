// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Storage;
using Onvif.Open.Core.Storage;
using Onvif.Open.Plugins.Implementation.Loading;
using Onvif.Open.Recording.Implementation.Storage;

namespace Onvif.Open.Recorder.Configuration
{
    /// <summary>
    /// Creates the archive store named by "Recording:Archive:Provider": the built-in FileSystem store or a storage plugin.
    /// </summary>
    public static class StorageConfiguration
    {
        #region Public Variables

        public const string FileSystemProvider = "FileSystem";

        #endregion

        #region Public Methods

        /// <summary>The configured archive, or null when recordings are only kept locally.</summary>
        public static ArchiveStore CreateArchive(IConfiguration configuration, string contentRoot, ILoggerFactory loggerFactory)
        {
            var recording = configuration.GetSection(RecorderOptions.SectionName);
            var options = recording.GetSection("Archive").Get<ArchiveOptions>() ?? new ArchiveOptions();

            if (!options.IsEnabled)
            {
                return null;
            }

            var provider = options.Provider.Trim();
            var settings = PluginConfiguration.ReadSettings(recording.GetSection("Archive").GetSection("Settings"));
            var logger = loggerFactory.CreateLogger(typeof(StorageConfiguration).FullName);

            if (string.Equals(provider, FileSystemProvider, StringComparison.OrdinalIgnoreCase))
            {
                var context = new SegmentStoreContext(settings);
                var path = Path.GetFullPath(Path.Combine(contentRoot, context.Require("Path")));
                logger.LogInformation("Archiving recordings to {Path}", path);
                return new ArchiveStore(FileSystemProvider, new FileSystemSegmentStore(path));
            }

            if (provider.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || provider.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Invalid storage provider folder name '{provider}'.");
            }

            var plugins = configuration.GetSection(PluginsOptions.SectionName).Get<PluginsOptions>() ?? new PluginsOptions();
            var folder = Path.Combine(Path.GetFullPath(Path.Combine(contentRoot, plugins.Directory ?? "plugins")), provider);
            var storeProvider = PluginLoader.LoadStorageProvider(folder);
            var store = storeProvider.Create(new SegmentStoreContext(settings, loggerFactory.CreateLogger(storeProvider.GetType().FullName)))
                ?? throw new InvalidOperationException($"Storage provider '{storeProvider.Name}' did not create a store.");

            logger.LogInformation("Archiving recordings with storage provider {Provider} to {Store}", storeProvider.Name, store);
            return new ArchiveStore(storeProvider.Name, store);
        }

        #endregion
    }

    /// <summary>
    /// An archive store and its provider name.
    /// </summary>
    public sealed class ArchiveStore
    {
        #region Constructor

        public ArchiveStore(string name, ISegmentStore store)
        {
            Name = name;
            Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        #endregion

        #region Public Variables

        public string Name { get; }
        public ISegmentStore Store { get; }

        #endregion
    }
}
