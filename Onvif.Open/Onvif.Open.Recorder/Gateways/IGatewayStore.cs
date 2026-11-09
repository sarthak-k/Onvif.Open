// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>
    /// Gateways in the settings database. Backup settings (they hold storage keys) are encrypted.
    /// </summary>
    public interface IGatewayStore
    {
        Task<IReadOnlyList<Gateway>> GetAsync(string organizationId, CancellationToken cancellationToken = default);
        Task<Gateway> FindAsync(string id, CancellationToken cancellationToken = default);
        Task<Gateway> FindEmbeddedAsync(CancellationToken cancellationToken = default);
        Task SaveAsync(Gateway gateway, CancellationToken cancellationToken = default);

        /// <summary>The paired gateway whose token has this hash, or null.</summary>
        Task<Gateway> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>Stores a heartbeat without touching the settings.</summary>
        Task ReportAsync(string id, string version, DateTime startedAt, DateTime seenAt, GatewayHealth health, CancellationToken cancellationToken = default);

        /// <summary>The organization this recorder serves (all-in-one: the only one), or null before setup.</summary>
        Task<string> FindOrganizationIdAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="IGatewayStore"/> on EF Core.
    /// </summary>
    public sealed class EfGatewayStore : IGatewayStore
    {
        public EfGatewayStore(IDbContextFactory<RecorderDbContext> contexts, IDataProtectionProvider dataProtection)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _backupSettings = (dataProtection ?? throw new ArgumentNullException(nameof(dataProtection))).CreateProtector("Onvif.Open.Recorder.BackupSettings.v1");
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly IDbContextFactory<RecorderDbContext> _contexts;
        private readonly IDataProtector _backupSettings;

        public async Task<IReadOnlyList<Gateway>> GetAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var records = await db.Gateways.AsNoTracking().Where(g => g.OrganizationId == organizationId).ToListAsync(cancellationToken).ConfigureAwait(false);
                return records.OrderByDescending(g => g.Embedded).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Select(ToGateway).ToList();
            }
        }

        public async Task<Gateway> FindAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = await db.Gateways.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken).ConfigureAwait(false);
                return record == null ? null : ToGateway(record);
            }
        }

        public async Task<Gateway> FindEmbeddedAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = await db.Gateways.AsNoTracking().Where(g => g.Embedded).OrderBy(g => g.CreatedAt).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return record == null ? null : ToGateway(record);
            }
        }

        public async Task<Gateway> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(tokenHash))
            {
                return null;
            }

            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = await db.Gateways.AsNoTracking().FirstOrDefaultAsync(g => g.TokenHash == tokenHash, cancellationToken).ConfigureAwait(false);
                return record == null ? null : ToGateway(record);
            }
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Gateways.Where(g => g.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        public async Task SaveAsync(Gateway gateway, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = await db.Gateways.FirstOrDefaultAsync(g => g.Id == gateway.Id, cancellationToken).ConfigureAwait(false);
                if (record == null)
                {
                    record = new GatewayRecord { Id = gateway.Id, CreatedAt = gateway.CreatedAt };
                    db.Gateways.Add(record);
                }

                record.OrganizationId = gateway.OrganizationId;
                record.Name = gateway.Name;
                record.Location = gateway.Location;
                record.Embedded = gateway.Embedded;
                record.TokenHash = gateway.TokenHash;
                record.MinFreeSpaceGB = gateway.Storage.MinFreeSpaceGB;
                record.MaxSizePerCameraGB = gateway.Storage.MaxSizePerCameraGB;
                record.MaxTotalSizeGB = gateway.Storage.MaxTotalSizeGB;
                record.BackupProvider = string.IsNullOrWhiteSpace(gateway.Backup.Provider) ? null : gateway.Backup.Provider.Trim();
                record.BackupPrefix = string.IsNullOrWhiteSpace(gateway.Backup.Prefix) ? null : gateway.Backup.Prefix.Trim();
                record.BackupKeepLocalCopyHours = gateway.Backup.KeepLocalCopyHours;
                record.BackupSettings = gateway.Backup.Settings == null || gateway.Backup.Settings.Count == 0
                    ? null
                    : _backupSettings.Protect(JsonSerializer.Serialize(gateway.Backup.Settings, Json));
                record.DiscoveryMulticast = gateway.Discovery.Multicast;
                record.DiscoveryAddresses = JsonSerializer.Serialize(gateway.Discovery.Addresses ?? new List<string>(), Json);
                record.UpdatedAt = gateway.UpdatedAt;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task ReportAsync(string id, string version, DateTime startedAt, DateTime seenAt, GatewayHealth health, CancellationToken cancellationToken = default)
        {
            var json = JsonSerializer.Serialize(health, Json);
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                await db.Gateways.Where(g => g.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(g => g.Version, version)
                    .SetProperty(g => g.StartedAt, startedAt)
                    .SetProperty(g => g.LastSeenAt, seenAt)
                    .SetProperty(g => g.Health, json), cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<string> FindOrganizationIdAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Organizations.AsNoTracking().OrderBy(o => o.CreatedAt).Select(o => o.Id).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private Gateway ToGateway(GatewayRecord record)
        {
            return new Gateway
            {
                Id = record.Id,
                OrganizationId = record.OrganizationId,
                Name = record.Name,
                Location = record.Location,
                Embedded = record.Embedded,
                Version = record.Version,
                StartedAt = record.StartedAt,
                LastSeenAt = record.LastSeenAt,
                Health = string.IsNullOrEmpty(record.Health) ? null : JsonSerializer.Deserialize<GatewayHealth>(record.Health, Json),
                Storage = new StorageLimits { MinFreeSpaceGB = record.MinFreeSpaceGB, MaxSizePerCameraGB = record.MaxSizePerCameraGB, MaxTotalSizeGB = record.MaxTotalSizeGB },
                Backup = new BackupSettings
                {
                    Provider = record.BackupProvider,
                    Prefix = record.BackupPrefix,
                    KeepLocalCopyHours = record.BackupKeepLocalCopyHours,
                    Settings = string.IsNullOrEmpty(record.BackupSettings)
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(_backupSettings.Unprotect(record.BackupSettings), Json), StringComparer.OrdinalIgnoreCase)
                },
                Discovery = new DiscoverySettings
                {
                    Multicast = record.DiscoveryMulticast,
                    Addresses = string.IsNullOrEmpty(record.DiscoveryAddresses) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(record.DiscoveryAddresses, Json)
                },
                CreatedAt = record.CreatedAt,
                UpdatedAt = record.UpdatedAt,
                TokenHash = record.TokenHash
            };
        }
    }
}
