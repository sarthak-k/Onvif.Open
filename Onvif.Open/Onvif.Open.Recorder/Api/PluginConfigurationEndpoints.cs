// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Store;

using Onvif.Open.Recorder.Security;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: which plugin folders run, for which cameras, with which settings. Changes apply when the recorder
    /// starts next. Setting values are write-only (they may be secrets): responses list their names.
    /// </summary>
    internal static class PluginConfigurationEndpoints
    {
        #region Public Variables

        public sealed class PluginRequest
        {
            public bool Enabled { get; set; } = true;

            /// <summary>Camera ids; empty: all cameras.</summary>
            public List<string> Cameras { get; set; } = new List<string>();

            /// <summary>Settings ("Key" or "Nested:Key" to value); null keeps the stored settings.</summary>
            public Dictionary<string, string> Settings { get; set; }
        }

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapPluginConfigurationApi(this IEndpointRouteBuilder app)
        {
            var plugins = app.MapGroup("/api/config/plugins").RequireAuthorization(Policies.Admin);

            plugins.MapGet("", async (ISettingsStore store, CancellationToken ct) => (await store.GetPluginsAsync(ct)).Select(ToDto));

            plugins.MapPut("/{folder}", async (string folder, PluginRequest request, ISettingsStore store, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(folder) || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || folder.Contains("..", StringComparison.Ordinal))
                {
                    return Results.BadRequest(new { errors = new[] { $"Invalid plugin folder name '{folder}'." } });
                }

                request = request ?? new PluginRequest();
                var cameras = (request.Cameras ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var known = (await store.GetCamerasAsync(ct)).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var unknown = cameras.Where(c => !known.Contains(c)).ToList();

                if (unknown.Count > 0)
                {
                    return Results.BadRequest(new { errors = new[] { $"Unknown camera(s): {string.Join(", ", unknown)}." } });
                }

                var existing = (await store.GetPluginsAsync(ct)).FirstOrDefault(p => string.Equals(p.Folder, folder, StringComparison.OrdinalIgnoreCase));
                var plugin = new StoredPlugin
                {
                    Folder = existing?.Folder ?? folder,
                    Enabled = request.Enabled,
                    Cameras = cameras,
                    Settings = request.Settings == null
                        ? existing?.Settings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(request.Settings, StringComparer.OrdinalIgnoreCase)
                };

                await store.SavePluginAsync(plugin, ct);
                return Results.Ok(ToDto(plugin));
            });

            plugins.MapDelete("/{folder}", async (string folder, ISettingsStore store, CancellationToken ct) =>
                await store.DeletePluginAsync(folder, ct) ? Results.NoContent() : Results.NotFound());

            return app;
        }

        #endregion

        #region Private Methods

        private static object ToDto(StoredPlugin plugin)
        {
            return new
            {
                folder = plugin.Folder,
                enabled = plugin.Enabled,
                cameras = plugin.Cameras,
                settings = plugin.Settings.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            };
        }

        #endregion
    }
}
