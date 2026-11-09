// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
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
    /// Creates the gateway's backup store: the built-in FileSystem store (a NAS share, another disk) or a storage plugin.
    /// </summary>
    public static class StorageConfiguration
    {
        #region Public Variables

        public const string FileSystemProvider = "FileSystem";

        #endregion

        #region Public Methods

        /// <summary>The backup store, or null when recordings are only kept on the gateway.</summary>
        /// <param name="configuration">For "Plugins:Directory", where storage plugins are.</param>
        public static ArchiveStore CreateArchive(Gateways.BackupSettings backup, IConfiguration configuration, string contentRoot, ILoggerFactory loggerFactory)
        {
            if (backup == null || !backup.IsEnabled)
            {
                return null;
            }

            var provider = backup.Provider.Trim();
            var settings = (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(backup.Settings ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            var keepLocal = TimeSpan.FromHours(Math.Max(0, backup.KeepLocalCopyHours));
            var logger = loggerFactory.CreateLogger(typeof(StorageConfiguration).FullName);

            if (string.Equals(provider, FileSystemProvider, StringComparison.OrdinalIgnoreCase))
            {
                var context = new SegmentStoreContext(settings);
                var path = Path.GetFullPath(Path.Combine(contentRoot, context.Require("Path")));
                logger.LogInformation("Archiving recordings to {Path}", path);
                return new ArchiveStore(FileSystemProvider, new FileSystemSegmentStore(path), backup.Prefix, keepLocal);
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
            return new ArchiveStore(storeProvider.Name, store, backup.Prefix, keepLocal);
        }

        #endregion
    }

    /// <summary>
    /// An archive store and its provider name.
    /// </summary>
    public sealed class ArchiveStore
    {
        #region Constructor

        public ArchiveStore(string name, ISegmentStore store, string prefix = null, TimeSpan keepLocalCopyFor = default)
        {
            Name = name;
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Prefix = prefix;
            KeepLocalCopyFor = keepLocalCopyFor;
        }

        #endregion

        #region Public Variables

        public string Name { get; }
        public ISegmentStore Store { get; }

        /// <summary>Prepended to every key, e.g. "site-a".</summary>
        public string Prefix { get; }

        /// <summary>How long the local copy is kept after upload.</summary>
        public TimeSpan KeepLocalCopyFor { get; }

        #endregion
    }
}
