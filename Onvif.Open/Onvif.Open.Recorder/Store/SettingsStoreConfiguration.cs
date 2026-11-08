// Sarthak Killedar
// 2026-11-08

using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// "Settings" section: where the settings database and its encryption keys live, and the one-time import of the
    /// cameras and plugins of the configuration file.
    /// </summary>
    public static class SettingsStoreConfiguration
    {
        #region Public Variables

        public const string SectionName = "Settings";

        #endregion

        #region Public Methods

        /// <summary>The database file, relative to the content root. Default: data/onvif-open.db.</summary>
        public static string GetDatabasePath(IConfiguration configuration, string contentRoot)
        {
            return Path.GetFullPath(Path.Combine(contentRoot, configuration.GetSection(SectionName)["Database"] ?? Path.Combine("data", "onvif-open.db")));
        }

        /// <summary>The Data Protection key folder. Default: "keys" next to the database.</summary>
        public static string GetKeysDirectory(IConfiguration configuration, string contentRoot)
        {
            var configured = configuration.GetSection(SectionName)["KeysDirectory"];
            return configured == null
                ? Path.Combine(Path.GetDirectoryName(GetDatabasePath(configuration, contentRoot)), "keys")
                : Path.GetFullPath(Path.Combine(contentRoot, configured));
        }

        /// <summary>
        /// Opens the store. On the first start, the cameras ("Recording:Cameras") and plugins ("Plugins:Items") of the
        /// configuration file are validated and imported; from then on the store is the source of truth.
        /// </summary>
        public static ISettingsStore Open(IConfiguration configuration, string contentRoot, IDataProtectionProvider dataProtection, ILogger logger)
        {
            var path = GetDatabasePath(configuration, contentRoot);
            var store = new SqliteSettingsStore(path, dataProtection);
            var fileOptions = configuration.GetSection(RecorderOptions.SectionName).Get<RecorderOptions>() ?? new RecorderOptions();

            if (!store.IsImported)
            {
                // A broken file must be fixed before anything is imported, so nothing half-valid ends up in the store.
                RecordingConfiguration.Validate(fileOptions);
                var plugins = PluginConfiguration.ReadPlugins(configuration);
                store.Import(fileOptions.Cameras, plugins);
                logger.LogInformation("Imported {Cameras} camera(s) and {Plugins} plugin(s) from the configuration into {Database}", fileOptions.Cameras.Count, plugins.Count, path);
            }
            else if (fileOptions.Cameras.Count > 0)
            {
                logger.LogInformation("Cameras are managed in {Database}: Recording:Cameras in the configuration was imported on the first start and is ignored now", path);
            }

            return store;
        }

        #endregion
    }
}
