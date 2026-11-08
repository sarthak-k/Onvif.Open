// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Plugins.Implementation.Builder;
using Onvif.Open.Plugins.Implementation.Loading;

namespace Onvif.Open.Recorder.Configuration
{
    /// <summary>
    /// Loads the plugins listed in the "Plugins" section and builds the plugin host.
    /// </summary>
    public static class PluginConfiguration
    {
        #region Public Methods

        /// <summary>
        /// Builds the plugin host for <paramref name="plugins"/> (from the settings store); the "Plugins" section supplies
        /// the directory and queue sizes.
        /// </summary>
        public static IPluginHost CreateHost(IConfiguration configuration, string contentRoot, IReadOnlyList<Store.StoredPlugin> plugins, IEnumerable<string> cameraIds, ConfiguredRecording recording, ILoggerFactory loggerFactory)
        {
            var section = configuration.GetSection(PluginsOptions.SectionName);
            var options = section.Get<PluginsOptions>() ?? new PluginsOptions();
            var logger = loggerFactory.CreateLogger(typeof(PluginConfiguration).FullName);
            var knownCameras = new HashSet<string>(cameraIds ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            var builder = PluginHostBuilder.GetBuilder()
                .SetRecordingManager(recording.Manager)
                .SetSnapshotProvider(recording.GetSnapshotAsync)
                .SetPtzProvider(recording.GetPtz)
                .SetImagingProvider(recording.GetImaging)
                .SetIoProvider(recording.GetIo)
                .SetDeviceRecordingsProvider(recording.GetDeviceRecordings)
                .SetFrameQueueCapacity(options.FrameQueueCapacity)
                .SetEventHistoryCapacity(options.EventHistory)
                .SetStopTimeout(options.StopTimeout)
                .SetLoggerFactory(loggerFactory);

            var directory = Path.GetFullPath(Path.Combine(contentRoot, options.Directory ?? "plugins"));

            foreach (var item in (plugins ?? Array.Empty<Store.StoredPlugin>()).Where(p => p.Enabled))
            {
                var folderName = item.Folder;

                if (folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folderName.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Invalid plugin folder name '{folderName}'.");
                }

                var cameras = item.Cameras?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();
                var unknown = cameras.Where(c => !knownCameras.Contains(c)).ToList();

                if (unknown.Count > 0)
                {
                    throw new InvalidOperationException($"Plugin '{folderName}' refers to unknown camera(s): {string.Join(", ", unknown)}.");
                }

                var loaded = PluginLoader.LoadFromFolder(Path.Combine(directory, folderName));
                var settings = (IReadOnlyDictionary<string, string>)item.Settings ?? new Dictionary<string, string>();

                builder.AddPlugins(loaded, cameras.Count == 0 ? null : cameras, settings);
                logger.LogInformation("Loaded plugin(s) {Plugins} from {Folder}", string.Join(", ", loaded.Select(p => p.Name)), folderName);
            }

            return builder.Build();
        }

        /// <summary>The "Plugins:Items" of the configuration file, for the one-time import into the settings store.</summary>
        public static IReadOnlyList<Store.StoredPlugin> ReadPlugins(IConfiguration configuration)
        {
            var section = configuration.GetSection(PluginsOptions.SectionName);
            var options = section.Get<PluginsOptions>() ?? new PluginsOptions();

            return options.Items.Select(item => new Store.StoredPlugin
            {
                Folder = item.Key,
                Enabled = item.Value?.Enabled != false,
                Cameras = item.Value?.Cameras?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>(),
                Settings = new Dictionary<string, string>(ReadSettings(section.GetSection("Items").GetSection(item.Key).GetSection("Settings")), StringComparer.OrdinalIgnoreCase)
            }).ToList();
        }

        /// <summary>Flattens a configuration section into "Key" / "Nested:Key" pairs.</summary>
        public static IReadOnlyDictionary<string, string> ReadSettings(IConfigurationSection settings)
        {
            return settings.AsEnumerable(makePathsRelative: true)
                .Where(pair => pair.Value != null)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        }

        #endregion
    }
}
