// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>
    /// A change to a gateway's settings. Null sections are kept; null backup settings keep the stored ones (they are
    /// write-only secrets).
    /// </summary>
    public sealed class GatewayRequest
    {
        public string Name { get; set; }
        public StorageLimits Storage { get; set; }
        public BackupRequest Backup { get; set; }
        public DiscoverySettings Discovery { get; set; }
    }

    /// <summary>The cloud backup in a <see cref="GatewayRequest"/>; empty provider: no backup.</summary>
    public sealed class BackupRequest
    {
        public string Provider { get; set; }
        public string Prefix { get; set; }
        public int KeepLocalCopyHours { get; set; }

        /// <summary>Null: keep the stored settings (when the provider stays the same).</summary>
        public Dictionary<string, string> Settings { get; set; }
    }

    /// <summary>
    /// The organization's gateways and the one inside this recorder (all-in-one), whose settings the recording, its
    /// retention and discovery read. The embedded gateway is created on first need once the organization exists.
    /// </summary>
    public sealed class GatewayService
    {
        #region Constructor

        public GatewayService(IGatewayStore store, IConfiguration configuration, TimeProvider timeProvider, ILogger<GatewayService> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _timeProvider = timeProvider ?? TimeProvider.System;
            _logger = logger;
            _name = configuration?["Gateway:Name"];
            _location = GatewayLocations.Normalize(configuration?["Gateway:Location"]);
            StartedAt = _timeProvider.GetUtcNow().UtcDateTime;
        }

        #endregion

        #region Public Variables

        public const int MaxNameLength = 100;
        public const int MaxAddresses = 64;
        public const int MaxKeepLocalCopyHours = 24 * 365;

        /// <summary>A gateway is online while its last heartbeat is younger than this.</summary>
        public static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(2);

        public static string Version { get; } = ReadVersion();

        public DateTime StartedAt { get; }

        /// <summary>The embedded gateway as last loaded or saved; null before the first-run setup.</summary>
        public Gateway Local => Volatile.Read(ref _local);

        /// <summary>The storage limits recording applies now (defaults until the gateway exists).</summary>
        public StorageLimits Limits => Local?.Storage ?? DefaultLimits;

        public DiscoverySettings Discovery => Local?.Discovery ?? DefaultDiscovery;

        /// <summary>The backup the recorder started with; a different saved one applies at the next start.</summary>
        public BackupSettings RunningBackup { get; private set; } = new BackupSettings();

        #endregion

        #region Private Variables

        private static readonly StorageLimits DefaultLimits = new StorageLimits();
        private static readonly DiscoverySettings DefaultDiscovery = new DiscoverySettings();
        private readonly IGatewayStore _store;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly string _name;
        private readonly string _location;
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);
        private Gateway _local;

        #endregion

        #region Public Methods

        /// <summary>
        /// The embedded gateway, created when the organization exists and it does not yet; null before setup.
        /// </summary>
        public async Task<Gateway> EnsureLocalAsync(CancellationToken cancellationToken = default)
        {
            if (Local != null)
            {
                return Local;
            }

            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Local != null)
                {
                    return Local;
                }

                var gateway = await _store.FindEmbeddedAsync(cancellationToken).ConfigureAwait(false);
                if (gateway == null)
                {
                    var organizationId = await _store.FindOrganizationIdAsync(cancellationToken).ConfigureAwait(false);
                    if (organizationId == null)
                    {
                        return null;
                    }

                    var now = _timeProvider.GetUtcNow().UtcDateTime;
                    gateway = new Gateway
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        OrganizationId = organizationId,
                        Name = Truncate(string.IsNullOrWhiteSpace(_name) ? Environment.MachineName : _name.Trim(), MaxNameLength),
                        Location = _location,
                        Embedded = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    await _store.SaveAsync(gateway, cancellationToken).ConfigureAwait(false);
                    _logger?.LogInformation("Gateway {Name} ({Id}) created for this recorder", gateway.Name, gateway.Id);
                }

                Volatile.Write(ref _local, gateway);
                return gateway;
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Loads the embedded gateway at startup and remembers the backup the recorder starts with.</summary>
        public async Task<Gateway> StartAsync(CancellationToken cancellationToken = default)
        {
            var gateway = await EnsureLocalAsync(cancellationToken).ConfigureAwait(false);
            RunningBackup = gateway?.Backup ?? new BackupSettings();
            return gateway;
        }

        public Task<IReadOnlyList<Gateway>> GetAsync(string organizationId, CancellationToken cancellationToken = default) =>
            _store.GetAsync(organizationId, cancellationToken);

        /// <summary>The gateway when it belongs to the organization, else null.</summary>
        public async Task<Gateway> FindAsync(string organizationId, string id, CancellationToken cancellationToken = default)
        {
            var gateway = await _store.FindAsync(id, cancellationToken).ConfigureAwait(false);
            return gateway?.OrganizationId == organizationId ? gateway : null;
        }

        /// <summary>Whether the saved backup differs from the one running (it applies when the recorder restarts).</summary>
        public bool IsRestartRequired(Gateway gateway) =>
            gateway?.Embedded == true && !SameBackup(gateway.Backup, RunningBackup);

        public async Task<ServiceResult<Gateway>> UpdateAsync(string organizationId, string id, GatewayRequest request, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var gateway = await _store.FindAsync(id, cancellationToken).ConfigureAwait(false);
                if (gateway == null || gateway.OrganizationId != organizationId)
                {
                    return ServiceResult<Gateway>.NotFound();
                }

                request = request ?? new GatewayRequest();
                var errors = new List<string>();

                if (request.Name != null)
                {
                    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength)
                    {
                        errors.Add($"Name is required and at most {MaxNameLength} characters.");
                    }
                    else
                    {
                        gateway.Name = request.Name.Trim();
                    }
                }

                if (request.Storage != null)
                {
                    CheckLimit(request.Storage.MinFreeSpaceGB, "Free space to keep", errors);
                    CheckLimit(request.Storage.MaxSizePerCameraGB, "Space per camera", errors);
                    CheckLimit(request.Storage.MaxTotalSizeGB, "Total space", errors);
                    gateway.Storage = new StorageLimits
                    {
                        MinFreeSpaceGB = Positive(request.Storage.MinFreeSpaceGB),
                        MaxSizePerCameraGB = Positive(request.Storage.MaxSizePerCameraGB),
                        MaxTotalSizeGB = Positive(request.Storage.MaxTotalSizeGB)
                    };
                }

                if (request.Backup != null)
                {
                    gateway.Backup = PrepareBackup(request.Backup, gateway.Backup, errors);
                }

                if (request.Discovery != null)
                {
                    gateway.Discovery = PrepareDiscovery(request.Discovery, errors);
                }

                if (errors.Count > 0)
                {
                    return ServiceResult<Gateway>.Invalid(errors);
                }

                gateway.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;
                await _store.SaveAsync(gateway, cancellationToken).ConfigureAwait(false);

                if (gateway.Embedded)
                {
                    Volatile.Write(ref _local, gateway);
                }

                _logger?.LogInformation("Gateway {Name} ({Id}) changed", gateway.Name, gateway.Id);
                return ServiceResult<Gateway>.Ok(gateway);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Stores the embedded gateway's heartbeat.</summary>
        public async Task ReportAsync(GatewayHealth health, CancellationToken cancellationToken = default)
        {
            var gateway = await EnsureLocalAsync(cancellationToken).ConfigureAwait(false);
            if (gateway == null)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await _store.ReportAsync(gateway.Id, Version, StartedAt, now, health, cancellationToken).ConfigureAwait(false);
            gateway.Version = Version;
            gateway.StartedAt = StartedAt;
            gateway.LastSeenAt = now;
            gateway.Health = health;
        }

        public bool IsOnline(Gateway gateway) =>
            gateway?.LastSeenAt is DateTime seen && _timeProvider.GetUtcNow().UtcDateTime - seen < OnlineWindow;

        /// <summary>The recorder-wide retention limits as used by recording.</summary>
        public static RetentionOptions ToRetentionOptions(StorageLimits limits) => new RetentionOptions
        {
            MinFreeSpaceGB = limits?.MinFreeSpaceGB,
            MaxSizePerCameraGB = limits?.MaxSizePerCameraGB,
            MaxTotalSizeGB = limits?.MaxTotalSizeGB
        };

        #endregion

        #region Private Methods

        private static BackupSettings PrepareBackup(BackupRequest requested, BackupSettings stored, List<string> errors)
        {
            var provider = string.IsNullOrWhiteSpace(requested.Provider) ? null : requested.Provider.Trim();
            var sameProvider = string.Equals(provider, stored?.Provider, StringComparison.OrdinalIgnoreCase);
            var settings = requested.Settings != null
                ? new Dictionary<string, string>(requested.Settings.Where(s => !string.IsNullOrWhiteSpace(s.Key)), StringComparer.OrdinalIgnoreCase)
                : sameProvider ? new Dictionary<string, string>(stored?.Settings ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (provider != null)
            {
                if (provider.Length > 200 || provider.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || provider.Contains("..", StringComparison.Ordinal))
                {
                    errors.Add($"Backup provider '{provider}' is not a valid name.");
                }

                if (string.Equals(provider, StorageConfiguration.FileSystemProvider, StringComparison.OrdinalIgnoreCase)
                    && (!settings.TryGetValue("Path", out var path) || string.IsNullOrWhiteSpace(path)))
                {
                    errors.Add("A backup to a folder or NAS share needs its path (setting \"Path\").");
                }
            }

            if (requested.KeepLocalCopyHours < 0 || requested.KeepLocalCopyHours > MaxKeepLocalCopyHours)
            {
                errors.Add($"Hours to keep the gateway's copy must be between 0 and {MaxKeepLocalCopyHours}.");
            }

            if (requested.Prefix?.Trim().Length > 200)
            {
                errors.Add("The backup prefix is at most 200 characters.");
            }

            return provider == null
                ? new BackupSettings()
                : new BackupSettings { Provider = provider, Prefix = string.IsNullOrWhiteSpace(requested.Prefix) ? null : requested.Prefix.Trim(), KeepLocalCopyHours = requested.KeepLocalCopyHours, Settings = settings };
        }

        private static DiscoverySettings PrepareDiscovery(DiscoverySettings requested, List<string> errors)
        {
            var addresses = (requested.Addresses ?? new List<string>())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var address in addresses.Where(a => !IPEndPoint.TryParse(a, out _)))
            {
                errors.Add($"'{address}' is not an IP address (optionally with a port, e.g. 10.0.5.20:3702).");
            }

            if (addresses.Count > MaxAddresses)
            {
                errors.Add($"At most {MaxAddresses} addresses can be probed.");
            }

            return new DiscoverySettings { Multicast = requested.Multicast, Addresses = addresses };
        }

        private static void CheckLimit(double? value, string label, List<string> errors)
        {
            if (value is double gb && (double.IsNaN(gb) || gb < 0 || gb > 1_000_000))
            {
                errors.Add($"{label} must be between 0 and 1,000,000 GB.");
            }
        }

        private static double? Positive(double? value) => value is double gb && gb > 0 ? gb : null;

        private static bool SameBackup(BackupSettings a, BackupSettings b) =>
            JsonSerializer.Serialize(Normalize(a)) == JsonSerializer.Serialize(Normalize(b));

        private static object Normalize(BackupSettings backup) => backup == null || !backup.IsEnabled
            ? null
            : new { backup.Provider, backup.Prefix, backup.KeepLocalCopyHours, Settings = (backup.Settings ?? new Dictionary<string, string>()).OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase).ToList() };

        private static string Truncate(string value, int length) => value.Length <= length ? value : value.Substring(0, length);

        private static string ReadVersion()
        {
            var version = typeof(GatewayService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? typeof(GatewayService).Assembly.GetName().Version?.ToString() ?? "0.0.0";
            var build = version.IndexOf('+');
            return build > 0 && version.Length > build + 8 ? version.Substring(0, build + 8) : version;
        }

        #endregion
    }
}
