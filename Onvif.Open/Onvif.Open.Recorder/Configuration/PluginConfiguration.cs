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

        public static IPluginHost CreateHost(IConfiguration configuration, string contentRoot, RecorderOptions recorderOptions, ConfiguredRecording recording, ILoggerFactory loggerFactory)
        {
            var section = configuration.GetSection(PluginsOptions.SectionName);
            var options = section.Get<PluginsOptions>() ?? new PluginsOptions();
            var logger = loggerFactory.CreateLogger(typeof(PluginConfiguration).FullName);
            var knownCameras = new HashSet<string>(recorderOptions.Cameras.Select(c => c.Id), StringComparer.OrdinalIgnoreCase);

            var builder = PluginHostBuilder.GetBuilder()
                .SetRecordingManager(recording.Manager)
                .SetSnapshotProvider(recording.GetSnapshotAsync)
                .SetPtzProvider(recording.GetPtz)
                .SetImagingProvider(recording.GetImaging)
                .SetIoProvider(recording.GetIo)
                .SetFrameQueueCapacity(options.FrameQueueCapacity)
                .SetEventHistoryCapacity(options.EventHistory)
                .SetStopTimeout(options.StopTimeout)
                .SetLoggerFactory(loggerFactory);

            var directory = Path.GetFullPath(Path.Combine(contentRoot, options.Directory ?? "plugins"));

            foreach (var item in options.Items.Where(i => i.Value?.Enabled != false))
            {
                var folderName = item.Key;

                if (folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folderName.Contains("..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Invalid plugin folder name '{folderName}'.");
                }

                var cameras = item.Value?.Cameras?.Where(c => !string.IsNullOrWhiteSpace(c)).ToList() ?? new List<string>();
                var unknown = cameras.Where(c => !knownCameras.Contains(c)).ToList();

                if (unknown.Count > 0)
                {
                    throw new InvalidOperationException($"Plugin '{folderName}' refers to unknown camera(s): {string.Join(", ", unknown)}.");
                }

                var plugins = PluginLoader.LoadFromFolder(Path.Combine(directory, folderName));
                var settings = ReadSettings(section.GetSection("Items").GetSection(folderName).GetSection("Settings"));

                builder.AddPlugins(plugins, cameras.Count == 0 ? null : cameras, settings);
                logger.LogInformation("Loaded plugin(s) {Plugins} from {Folder}", string.Join(", ", plugins.Select(p => p.Name)), folderName);
            }

            return builder.Build();
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
