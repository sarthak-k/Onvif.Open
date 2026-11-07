// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Recorder.Configuration
{
    /// <summary>
    /// The "Recording" section of appsettings.json.
    /// </summary>
    public sealed class RecorderOptions
    {
        #region Public Variables

        public const string SectionName = "Recording";

        public string StorageDirectory { get; set; } = "recordings";

        /// <summary>"MpegTs" (default) or "ElementaryStream".</summary>
        public string Format { get; set; } = "MpegTs";

        public RotationOptions Rotation { get; set; } = new RotationOptions();

        public RetentionOptions Retention { get; set; } = new RetentionOptions();

        /// <summary>Optional long-term storage (cloud, NAS) that completed segments are uploaded to.</summary>
        public ArchiveOptions Archive { get; set; } = new ArchiveOptions();

        public TimeSpan RetentionInterval { get; set; } = TimeSpan.FromMinutes(1);

        public TimeSpan StallTimeout { get; set; } = TimeSpan.FromSeconds(15);

        public TimeSpan MinReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

        public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromMinutes(1);

        public List<CameraOptions> Cameras { get; set; } = new List<CameraOptions>();

        #endregion
    }

    /// <summary>
    /// The "Discovery" section of appsettings.json.
    /// </summary>
    public sealed class DiscoveryOptions
    {
        #region Public Variables

        public const string SectionName = "Discovery";

        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);

        public bool Multicast { get; set; } = true;

        /// <summary>Addresses probed directly ("10.0.5.20" or "10.0.5.20:3702"), for cameras multicast cannot reach.</summary>
        public List<string> Targets { get; set; } = new List<string>();

        #endregion
    }

    /// <summary>
    /// The "Plugins" section of appsettings.json.
    /// </summary>
    public sealed class PluginsOptions
    {
        #region Public Variables

        public const string SectionName = "Plugins";

        /// <summary>Folder holding one sub folder per plugin: {Directory}/{PluginFolder}/{PluginFolder}.dll.</summary>
        public string Directory { get; set; } = "plugins";

        public int FrameQueueCapacity { get; set; } = 32;

        public int EventHistory { get; set; } = 1000;

        public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>Plugins to load, keyed by plugin folder name. Folders not listed (or disabled) are never loaded.</summary>
        public Dictionary<string, PluginItemOptions> Items { get; set; } = new Dictionary<string, PluginItemOptions>(StringComparer.OrdinalIgnoreCase);

        #endregion
    }

    public sealed class PluginItemOptions
    {
        #region Public Variables

        public bool Enabled { get; set; } = true;

        /// <summary>Camera ids the plugin may use; all cameras when empty.</summary>
        public List<string> Cameras { get; set; } = new List<string>();

        #endregion
    }

    /// <summary>
    /// "Analytics": the camera's own object detection (ONVIF Profile M metadata stream).
    /// </summary>
    public sealed class CameraAnalyticsOptions
    {
        #region Public Variables

        /// <summary>Receive detected objects and pass them to plugins (IAnalyticsObserver). Default: off.</summary>
        public bool Enabled { get; set; }

        /// <summary>Object classes that trigger recording in triggered mode, e.g. "Human", "Vehicle" (implies Enabled).</summary>
        public List<string> TriggerClasses { get; set; } = new List<string>();

        /// <summary>Minimum likelihood (0..1) for TriggerClasses. Default: 0.5.</summary>
        public double MinLikelihood { get; set; } = 0.5;

        /// <summary>Media profile with the metadata configuration; the first one that has one when empty.</summary>
        public string ProfileToken { get; set; }

        #endregion
    }

    public sealed class CameraEventOptions
    {
        #region Public Variables

        /// <summary>Subscribe to the camera's ONVIF events (ONVIF cameras only). Default: on.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Topics that trigger recording in triggered mode; the common motion topics when empty (unless analytics
        /// TriggerClasses are configured).
        /// </summary>
        public List<string> TriggerTopics { get; set; } = new List<string>();

        #endregion
    }

    /// <summary>
    /// A new file is started (at the next key frame) as soon as any configured limit is reached.
    /// </summary>
    public sealed class RotationOptions
    {
        #region Public Variables

        public TimeSpan? SegmentDuration { get; set; }

        public double? MaxSegmentSizeMB { get; set; }

        /// <summary>Start a new file on wall-clock boundaries, e.g. "01:00:00" for one file per clock hour.</summary>
        public TimeSpan? AlignToClock { get; set; }

        public bool IsEmpty => !SegmentDuration.HasValue && !MaxSegmentSizeMB.HasValue && !AlignToClock.HasValue;

        #endregion
    }

    /// <summary>
    /// "Recording:Archive": where completed segments are stored long term. Without a provider, recordings stay
    /// in StorageDirectory. With one, StorageDirectory is a local buffer and Retention applies to the archive.
    /// </summary>
    public sealed class ArchiveOptions
    {
        #region Public Variables

        /// <summary>
        /// "FileSystem" (built in: a NAS share or another disk, Settings:Path) or the folder name of a storage
        /// plugin in Plugins:Directory, e.g. "Onvif.Open.Storage.S3". Empty: no archive.
        /// </summary>
        public string Provider { get; set; }

        /// <summary>Prepended to every key, e.g. "site-a".</summary>
        public string Prefix { get; set; }

        /// <summary>How long the local copy is kept after upload. Default: removed right away.</summary>
        public TimeSpan KeepLocalCopyFor { get; set; } = TimeSpan.Zero;

        public int MaxConcurrentUploads { get; set; } = 2;

        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(15);

        // Settings ("Recording:Archive:Settings") are read as a flat dictionary by StorageConfiguration.

        public bool IsEnabled => !string.IsNullOrWhiteSpace(Provider);

        #endregion
    }

    /// <summary>
    /// A segment is deleted as soon as any configured rule selects it (oldest first).
    /// With an archive, the rules apply to the archive and MinFreeSpaceGB protects the local buffer.
    /// </summary>
    public sealed class RetentionOptions
    {
        #region Public Variables

        public TimeSpan? MaxAge { get; set; }

        public double? MaxTotalSizeGB { get; set; }

        public double? MaxSizePerCameraGB { get; set; }

        public double? MinFreeSpaceGB { get; set; } = 1;

        #endregion
    }

    public sealed class CameraOptions
    {
        #region Public Variables

        /// <summary>Folder and file name prefix: letters, digits, '-' and '_'.</summary>
        public string Id { get; set; }

        public bool Enabled { get; set; } = true;

        /// <summary>ONVIF device address (e.g. http://192.168.1.10). Either this or <see cref="RtspUrl"/>.</summary>
        public string OnvifUrl { get; set; }

        /// <summary>Plain RTSP stream, for cameras without ONVIF.</summary>
        public string RtspUrl { get; set; }

        public string Username { get; set; }

        public string Password { get; set; }

        /// <summary>ONVIF media profile; the first profile when empty.</summary>
        public string ProfileToken { get; set; }

        public StreamTransport Transport { get; set; } = StreamTransport.Rtsp;

        /// <summary>"Auto" (default: Media2 when available), "Media1" or "Media2".</summary>
        public MediaServiceVersion MediaVersion { get; set; } = MediaServiceVersion.Auto;

        /// <summary>Overrides the global rotation for this camera.</summary>
        public RotationOptions Rotation { get; set; }

        /// <summary>"Continuous" (default) or "Triggered" (record only around events and triggers).</summary>
        public Onvif.Open.Core.Recording.RecordingMode Mode { get; set; } = Onvif.Open.Core.Recording.RecordingMode.Continuous;

        public TimeSpan PreRoll { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan PostRoll { get; set; } = TimeSpan.FromSeconds(10);

        public CameraEventOptions Events { get; set; } = new CameraEventOptions();

        public CameraAnalyticsOptions Analytics { get; set; } = new CameraAnalyticsOptions();

        #endregion
    }
}
