// Sarthak Killedar
// 2026-11-08

using System.Collections.Generic;
using System.Linq;
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

        private StartupSettings(IReadOnlyList<CameraOptions> cameras, IReadOnlyList<StoredPlugin> plugins, Gateways.Gateway gateway)
        {
            Cameras = cameras;
            Plugins = plugins;
            Gateway = gateway;
        }

        #endregion

        #region Public Variables

        /// <summary>Every camera (the directory knows them all).</summary>
        public IReadOnlyList<CameraOptions> Cameras { get; }

        /// <summary>The cameras this recorder records itself: those of its own gateway (others record the rest).</summary>
        public IReadOnlyList<CameraOptions> LocalCameras =>
            Cameras.Where(c => c.GatewayId == null || (Gateway != null && string.Equals(c.GatewayId, Gateway.Id, System.StringComparison.OrdinalIgnoreCase))).ToList();
        public IReadOnlyList<StoredPlugin> Plugins { get; }

        /// <summary>The recorder's own gateway (its backup applies from the start); null before the first-run setup.</summary>
        public Gateways.Gateway Gateway { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Creates missing tables, then reads the recorder's gateway, the cameras (with their policies applied, filling
        /// <paramref name="directory"/>) and plugins.
        /// </summary>
        public static async Task<StartupSettings> LoadAsync(IDbContextFactory<RecorderDbContext> contexts, ISettingsStore store, Tenancy.ITenancyStore tenancy, Tenancy.CameraDirectory directory, Gateways.GatewayService gateways, Cases.CaseHolds holds = null, CancellationToken cancellationToken = default)
        {
            await DatabaseConfiguration.EnsureCreatedAsync(contexts, cancellationToken).ConfigureAwait(false);
            if (holds != null)
            {
                await holds.ReloadAsync(cancellationToken).ConfigureAwait(false);
            }

            var gateway = await gateways.StartAsync(cancellationToken).ConfigureAwait(false);
            var cameras = Configuration.CameraConfigurationService.Resolve(
                await store.GetCamerasAsync(cancellationToken).ConfigureAwait(false),
                await tenancy.GetPoliciesAsync(null, cancellationToken).ConfigureAwait(false),
                directory);

            return new StartupSettings(cameras, await store.GetPluginsAsync(cancellationToken).ConfigureAwait(false), gateway);
        }

        #endregion
    }
}
