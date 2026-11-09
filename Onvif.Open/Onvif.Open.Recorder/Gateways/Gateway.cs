// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>
    /// What records a site's cameras: today the recorder itself (the embedded gateway of an all-in-one install), later
    /// also gateways paired from other machines or hosted in the cloud. Holds the settings that belong to the machine
    /// doing the recording, and what it last reported about its health.
    /// </summary>
    public sealed class Gateway
    {
        #region Public Variables

        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string Name { get; set; }

        /// <summary>Where it runs: <see cref="GatewayLocations.Local"/> or <see cref="GatewayLocations.Cloud"/>.</summary>
        public string Location { get; set; } = GatewayLocations.Local;

        /// <summary>The gateway inside this recorder (all-in-one); it cannot be removed.</summary>
        public bool Embedded { get; set; }

        public string Version { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public GatewayHealth Health { get; set; }

        public StorageLimits Storage { get; set; } = new StorageLimits();
        public BackupSettings Backup { get; set; } = new BackupSettings();
        public DiscoverySettings Discovery { get; set; } = new DiscoverySettings();

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        #endregion
    }

    public static class GatewayLocations
    {
        public const string Local = "Local";
        public const string Cloud = "Cloud";

        public static string Normalize(string location) =>
            string.Equals(location, Cloud, StringComparison.OrdinalIgnoreCase) ? Cloud : Local;
    }

    /// <summary>
    /// How much of the gateway's disk recordings may use; the oldest files go first. Applies on top of each camera's
    /// recording policy. Changes apply at the next retention run.
    /// </summary>
    public sealed class StorageLimits
    {
        /// <summary>Keep at least this much disk free (default 1 GB); null or 0: no limit.</summary>
        public double? MinFreeSpaceGB { get; set; } = 1;

        public double? MaxSizePerCameraGB { get; set; }

        public double? MaxTotalSizeGB { get; set; }
    }

    /// <summary>
    /// Cloud backup: completed recordings are copied to a NAS share ("FileSystem", setting "Path") or a storage plugin
    /// (e.g. "Onvif.Open.Storage.S3"). Applied when the gateway starts next.
    /// </summary>
    public sealed class BackupSettings
    {
        /// <summary>Empty: recordings stay on the gateway.</summary>
        public string Provider { get; set; }

        /// <summary>Prepended to every stored file name, e.g. "site-a".</summary>
        public string Prefix { get; set; }

        /// <summary>How long the gateway keeps its own copy after the backup (0: removed right away).</summary>
        public int KeepLocalCopyHours { get; set; }

        /// <summary>The provider's settings (bucket, keys, ...); stored encrypted and never returned.</summary>
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public bool IsEnabled => !string.IsNullOrWhiteSpace(Provider);
    }

    /// <summary>
    /// How the gateway finds cameras: multicast on its network, plus addresses probed directly (other subnets).
    /// </summary>
    public sealed class DiscoverySettings
    {
        public bool Multicast { get; set; } = true;

        /// <summary>"10.0.5.20" or "10.0.5.20:3702".</summary>
        public List<string> Addresses { get; set; } = new List<string>();
    }

    /// <summary>
    /// What the gateway reported at its last heartbeat.
    /// </summary>
    public sealed class GatewayHealth
    {
        public int Cameras { get; set; }
        public int Recording { get; set; }

        /// <summary>Cameras that should record but cannot (reconnecting or failed).</summary>
        public int Failing { get; set; }

        public long? FreeBytes { get; set; }
        public long RecordedBytes { get; set; }

        public string BackupProvider { get; set; }
        public int? PendingUploads { get; set; }
        public string BackupError { get; set; }
    }
}
