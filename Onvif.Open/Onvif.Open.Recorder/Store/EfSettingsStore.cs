// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// <see cref="ISettingsStore"/> on EF Core. Camera passwords and plugin settings are encrypted with ASP.NET Core
    /// Data Protection before they are saved, so the database never holds them in clear text.
    /// </summary>
    public sealed class EfSettingsStore : ISettingsStore
    {
        #region Constructor

        public EfSettingsStore(IDbContextFactory<RecorderDbContext> contexts, IDataProtectionProvider dataProtection)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _passwords = (dataProtection ?? throw new ArgumentNullException(nameof(dataProtection))).CreateProtector("Onvif.Open.Recorder.CameraPassword.v1");
            _pluginSettings = dataProtection.CreateProtector("Onvif.Open.Recorder.PluginSettings.v1");
        }

        #endregion

        #region Private Variables

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IDbContextFactory<RecorderDbContext> _contexts;
        private readonly IDataProtector _passwords;
        private readonly IDataProtector _pluginSettings;

        #endregion

        #region Public Methods

        public async Task<IReadOnlyList<CameraOptions>> GetCamerasAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var records = await db.Cameras.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                return records.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id, StringComparer.Ordinal).Select(ToCamera).ToList();
            }
        }

        public async Task<CameraOptions> GetCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = Key(cameraId);
                var record = await db.Cameras.AsNoTracking().FirstOrDefaultAsync(c => c.Id == key, cancellationToken).ConfigureAwait(false);
                return record == null ? null : ToCamera(record);
            }
        }

        public async Task SaveCameraAsync(CameraOptions camera, CancellationToken cancellationToken = default)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (string.IsNullOrEmpty(camera.Id) || string.IsNullOrEmpty(camera.OrganizationId) || string.IsNullOrEmpty(camera.SiteId) || string.IsNullOrEmpty(camera.PolicyId) || string.IsNullOrEmpty(camera.GatewayId))
                {
                    throw new ArgumentException("A camera needs an id, an organization, a site, a recording policy and a gateway.", nameof(camera));
                }

                var key = Key(camera.Id);
                var now = DateTime.UtcNow;
                var record = await db.Cameras.FirstOrDefaultAsync(c => c.Id == key, cancellationToken).ConfigureAwait(false);

                if (record == null)
                {
                    record = new CameraRecord { Id = key, CreatedAt = now };
                    db.Cameras.Add(record);
                }

                // The password is kept apart, encrypted; the options never contain it, nor what has its own column.
                var options = Copy(camera);
                options.Id = null;
                options.Password = null;
                options.Name = null;
                options.OrganizationId = null;
                options.SiteId = null;
                options.PolicyId = null;
                options.GatewayId = null;

                record.OrganizationId = camera.OrganizationId;
                record.SiteId = camera.SiteId;
                record.PolicyId = camera.PolicyId;
                record.GatewayId = camera.GatewayId;
                record.Name = string.IsNullOrWhiteSpace(camera.Name) ? camera.Id : camera.Name.Trim();
                record.Enabled = camera.Enabled;
                record.Options = JsonSerializer.Serialize(options, Json);
                record.Password = string.IsNullOrEmpty(camera.Password) ? null : _passwords.Protect(camera.Password);
                record.UpdatedAt = now;

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<bool> DeleteCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = Key(cameraId);
                return await db.Cameras.Where(c => c.Id == key).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        public async Task<IReadOnlyList<StoredPlugin>> GetPluginsAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var records = await db.Plugins.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                return records.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => new StoredPlugin
                {
                    Folder = r.Folder,
                    Enabled = r.Enabled,
                    Cameras = JsonSerializer.Deserialize<List<string>>(r.Cameras, Json) ?? new List<string>(),
                    Settings = r.Settings == null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(_pluginSettings.Unprotect(r.Settings), Json), StringComparer.OrdinalIgnoreCase)
                }).ToList();
            }
        }

        public async Task SavePluginAsync(StoredPlugin plugin, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(plugin?.Folder))
            {
                throw new ArgumentException("A plugin folder is required.", nameof(plugin));
            }

            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = Key(plugin.Folder);
                var record = await db.Plugins.FirstOrDefaultAsync(p => p.Key == key, cancellationToken).ConfigureAwait(false);

                if (record == null)
                {
                    record = new PluginRecord { Key = key, Folder = plugin.Folder };
                    db.Plugins.Add(record);
                }

                record.Enabled = plugin.Enabled;
                record.Cameras = JsonSerializer.Serialize(plugin.Cameras ?? new List<string>(), Json);
                record.Settings = plugin.Settings == null || plugin.Settings.Count == 0 ? null : _pluginSettings.Protect(JsonSerializer.Serialize(plugin.Settings, Json));
                record.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<bool> DeletePluginAsync(string folder, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var key = Key(folder);
                return await db.Plugins.Where(p => p.Key == key).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        #endregion

        #region Private Methods

        private static string Key(string id)
        {
            return (id ?? string.Empty).ToLowerInvariant();
        }

        private CameraOptions ToCamera(CameraRecord record)
        {
            var camera = JsonSerializer.Deserialize<CameraOptions>(record.Options, Json);
            camera.Id = record.Id;
            camera.OrganizationId = record.OrganizationId;
            camera.SiteId = record.SiteId;
            camera.PolicyId = record.PolicyId;
            camera.GatewayId = record.GatewayId;
            camera.Name = record.Name;
            camera.Enabled = record.Enabled;
            camera.Password = record.Password == null ? null : _passwords.Unprotect(record.Password);
            return camera;
        }

        private static CameraOptions Copy(CameraOptions camera)
        {
            return JsonSerializer.Deserialize<CameraOptions>(JsonSerializer.Serialize(camera, Json), Json);
        }

        #endregion
    }
}
