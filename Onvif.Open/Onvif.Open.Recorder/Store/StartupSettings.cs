// Sarthak Killedar
// 2026-11-08

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// The settings the recorder starts with: the database is created when needed, then the cameras and plugins are
    /// read once. Changes while running go through the API (and are applied directly).
    /// </summary>
    public sealed class StartupSettings
    {
        #region Constructor

        private StartupSettings(IReadOnlyList<CameraOptions> cameras, IReadOnlyList<StoredPlugin> plugins)
        {
            Cameras = cameras;
            Plugins = plugins;
        }

        #endregion

        #region Public Variables

        public IReadOnlyList<CameraOptions> Cameras { get; }
        public IReadOnlyList<StoredPlugin> Plugins { get; }

        #endregion

        #region Public Methods

        /// <summary>Creates missing tables, then reads the cameras (with their policies applied, filling <paramref name="directory"/>) and plugins.</summary>
        public static async Task<StartupSettings> LoadAsync(IDbContextFactory<RecorderDbContext> contexts, ISettingsStore store, Tenancy.ITenancyStore tenancy, Tenancy.CameraDirectory directory, CancellationToken cancellationToken = default)
        {
            await DatabaseConfiguration.EnsureCreatedAsync(contexts, cancellationToken).ConfigureAwait(false);
            var cameras = Configuration.CameraConfigurationService.Resolve(
                await store.GetCamerasAsync(cancellationToken).ConfigureAwait(false),
                await tenancy.GetPoliciesAsync(null, cancellationToken).ConfigureAwait(false),
                directory);

            return new StartupSettings(cameras, await store.GetPluginsAsync(cancellationToken).ConfigureAwait(false));
        }

        #endregion
    }
}
