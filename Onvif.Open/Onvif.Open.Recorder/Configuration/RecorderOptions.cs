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
    /// A segment is deleted as soon as any configured rule selects it (oldest first).
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

        /// <summary>Overrides the global rotation for this camera.</summary>
        public RotationOptions Rotation { get; set; }

        #endregion
    }
}
