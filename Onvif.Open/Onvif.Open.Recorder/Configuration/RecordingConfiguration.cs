// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recording.Implementation.Archive;
using Onvif.Open.Recording.Implementation.Builder;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Recording.Implementation.Policy;
using Onvif.Open.Recording.Implementation.Storage;
using Onvif.Open.Streaming.Implementation.Builder;

namespace Onvif.Open.Recorder.Configuration
{
    /// <summary>
    /// Turns <see cref="RecorderOptions"/> into a configured <see cref="IRecordingManager"/>.
    /// </summary>
    public static class RecordingConfiguration
    {
        #region Public Methods

        /// <summary>
        /// Creates the manager with <paramref name="cameras"/> (from the settings database) and keeps the ONVIF cameras,
        /// which also provide snapshots, PTZ, imaging, ...
        /// </summary>
        /// <param name="cameraRetention">How long each camera's video is kept (from its recording policy); null: no per-camera limit.</param>
        public static ConfiguredRecording Create(RecorderOptions options, IReadOnlyList<CameraOptions> cameras, ILoggerFactory loggerFactory, ArchiveStore archive = null, Func<string, TimeSpan?> cameraRetention = null)
        {
            Validate(options);
            ValidateCameras(cameras);

            var builder = RecordingManagerBuilder.GetBuilder();

            if (archive != null)
            {
                builder.SetArchive(archive.Store, new SegmentArchiveOptions
                {
                    Name = archive.Name,
                    Prefix = options.Archive?.Prefix,
                    KeepLocalCopyFor = options.Archive?.KeepLocalCopyFor ?? TimeSpan.Zero,
                    MaxConcurrentUploads = options.Archive?.MaxConcurrentUploads ?? 2,
                    RefreshInterval = options.Archive?.RefreshInterval ?? TimeSpan.FromMinutes(15),
                    MinLocalFreeBytes = options.Retention?.MinFreeSpaceGB is double free && free > 0 ? ToBytes(free, RotationPolicies.GiB) : 0
                });
            }

            var manager = builder
                .SetStorageDirectory(options.StorageDirectory)
                .SetFormat(ToFormat(options.Format))
                .SetRotationPolicy(ToRotationPolicy(options.Rotation) ?? RotationPolicies.Default)
                .SetRetentionPolicy(WithCameraRetention(ToRetentionPolicy(options.Retention), cameraRetention))
                .SetRetentionInterval(options.RetentionInterval)
                .SetStallTimeout(options.StallTimeout)
                .SetReconnectDelay(options.MinReconnectDelay, options.MaxReconnectDelay)
                .SetLoggerFactory(loggerFactory)
                .Build();

            var onvifCameras = new Dictionary<string, (ICamera Camera, string ProfileToken)>(StringComparer.OrdinalIgnoreCase);

            foreach (var camera in (cameras ?? Array.Empty<CameraOptions>()).Where(c => c.Enabled))
            {
                var onvifCamera = AddCamera(manager, camera);
                if (onvifCamera != null)
                {
                    onvifCameras[camera.Id] = (onvifCamera, string.IsNullOrWhiteSpace(camera.ProfileToken) ? null : camera.ProfileToken);
                }
            }

            return new ConfiguredRecording(manager, onvifCameras);
        }

        /// <summary>
        /// The recorder-wide limits (free space, total size) plus each camera's own retention, looked up at every run so
        /// a changed recording policy applies at the next one.
        /// </summary>
        public static IRetentionPolicy WithCameraRetention(IRetentionPolicy recorderWide, Func<string, TimeSpan?> cameraRetention)
        {
            if (cameraRetention == null)
            {
                return recorderWide;
            }

            var perCamera = RetentionPolicies.When(context => context.Segments.Where(s => cameraRetention(s.CameraId) is TimeSpan keep && context.Now - s.LastWriteTime > keep));
            return ReferenceEquals(recorderWide, RetentionPolicies.KeepAll) ? perCamera : RetentionPolicies.All(recorderWide, perCamera);
        }

        /// <summary>Problems with one camera's configuration (empty when it is valid).</summary>
        public static IReadOnlyList<string> ValidateCamera(CameraOptions camera)
        {
            var errors = new List<string>();

            if (camera == null)
            {
                errors.Add("A camera is required.");
                return errors;
            }

            if (!CameraIdentifier.IsValid(camera.Id))
            {
                errors.Add($"Camera id '{camera.Id}' is invalid: use 1-64 letters, digits, '-' or '_'.");
            }

            if (string.IsNullOrWhiteSpace(camera.OnvifUrl) == string.IsNullOrWhiteSpace(camera.RtspUrl))
            {
                errors.Add($"Camera '{camera.Id}' needs exactly one of OnvifUrl or RtspUrl.");
            }

            if (camera.Audio?.Enabled == true && camera.Transport != StreamTransport.Rtsp)
            {
                errors.Add($"Camera '{camera.Id}': Audio needs Transport Rtsp (RTP over UDP carries one track).");
            }

            var cameraAnalytics = camera.Analytics;
            if (cameraAnalytics != null && (cameraAnalytics.Enabled || cameraAnalytics.TriggerClasses?.Count > 0))
            {
                if (string.IsNullOrWhiteSpace(camera.OnvifUrl))
                {
                    errors.Add($"Camera '{camera.Id}': Analytics need an ONVIF camera (OnvifUrl).");
                }

                if (cameraAnalytics.MinLikelihood < 0 || cameraAnalytics.MinLikelihood > 1)
                {
                    errors.Add($"Camera '{camera.Id}': Analytics:MinLikelihood must be between 0 and 1.");
                }
            }

            if (!string.IsNullOrWhiteSpace(camera.RtspUrl) && (!Uri.TryCreate(camera.RtspUrl, UriKind.Absolute, out var rtsp) || !string.Equals(rtsp.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add($"Camera '{camera.Id}': RtspUrl must be an absolute rtsp:// URL.");
            }

            if (camera.PreRoll < TimeSpan.Zero || camera.PostRoll < TimeSpan.Zero)
            {
                errors.Add($"Camera '{camera.Id}': PreRoll and PostRoll must not be negative.");
            }

            return errors;
        }

        /// <summary>Throws when a camera is invalid or an id is used twice.</summary>
        public static void ValidateCameras(IReadOnlyList<CameraOptions> cameras)
        {
            var errors = new List<string>();

            foreach (var camera in cameras ?? Array.Empty<CameraOptions>())
            {
                errors.AddRange(ValidateCamera(camera));
            }

            foreach (var duplicate in (cameras ?? Array.Empty<CameraOptions>()).GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                errors.Add($"Camera id '{duplicate.Key}' is used more than once.");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Invalid cameras:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
            }
        }

        public static void Validate(RecorderOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(options.StorageDirectory))
            {
                errors.Add("Recording:StorageDirectory is required.");
            }

            if (options.Archive != null && options.Archive.IsEnabled
                && (options.Archive.KeepLocalCopyFor < TimeSpan.Zero || options.Archive.MaxConcurrentUploads < 1 || options.Archive.RefreshInterval <= TimeSpan.Zero))
            {
                errors.Add("Recording:Archive: KeepLocalCopyFor must not be negative, MaxConcurrentUploads must be at least 1 and RefreshInterval positive.");
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Invalid recording configuration:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
            }
        }

        public static IRotationPolicy ToRotationPolicy(RotationOptions options)
        {
            if (options == null || options.IsEmpty)
            {
                return null;
            }

            var policies = new List<IRotationPolicy>();

            if (options.SegmentDuration.HasValue)
            {
                policies.Add(RotationPolicies.Every(options.SegmentDuration.Value));
            }

            if (options.MaxSegmentSizeMB.HasValue)
            {
                policies.Add(RotationPolicies.MaxSize(ToBytes(options.MaxSegmentSizeMB.Value, RotationPolicies.MiB)));
            }

            if (options.AlignToClock.HasValue)
            {
                policies.Add(RotationPolicies.AlignedToClock(options.AlignToClock.Value));
            }

            return policies.Count == 1 ? policies[0] : RotationPolicies.Any(policies.ToArray());
        }

        public static IRetentionPolicy ToRetentionPolicy(RetentionOptions options)
        {
            var policies = new List<IRetentionPolicy>();

            if (options?.MaxAge is TimeSpan maxAge)
            {
                policies.Add(RetentionPolicies.KeepFor(maxAge));
            }

            if (options?.MaxTotalSizeGB is double total && total > 0)
            {
                policies.Add(RetentionPolicies.MaxTotalSize(ToBytes(total, RotationPolicies.GiB)));
            }

            if (options?.MaxSizePerCameraGB is double perCamera && perCamera > 0)
            {
                policies.Add(RetentionPolicies.MaxSizePerCamera(ToBytes(perCamera, RotationPolicies.GiB)));
            }

            if (options?.MinFreeSpaceGB is double free && free > 0)
            {
                policies.Add(RetentionPolicies.MinFreeSpace(ToBytes(free, RotationPolicies.GiB)));
            }

            return policies.Count == 0 ? RetentionPolicies.KeepAll : RetentionPolicies.All(policies.ToArray());
        }

        #endregion

        #region Private Methods

        public static CameraRecordingOptions ToRecordingOptions(CameraOptions camera)
        {
            var options = new CameraRecordingOptions
            {
                ProfileToken = string.IsNullOrWhiteSpace(camera.ProfileToken) ? null : camera.ProfileToken,
                Transport = camera.Transport,
                RotationPolicy = ToRotationPolicy(camera.Rotation),
                Mode = camera.Mode,
                PreRoll = camera.PreRoll,
                PostRoll = camera.PostRoll,
                SubscribeToEvents = camera.Events?.Enabled != false,
                Audio = camera.Audio?.Enabled == true
            };

            var analytics = camera.Analytics ?? new CameraAnalyticsOptions();
            var classes = analytics.TriggerClasses?.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList() ?? new List<string>();
            options.ReceiveAnalytics = analytics.Enabled;
            options.AnalyticsMinLikelihood = analytics.MinLikelihood;
            options.AnalyticsProfileToken = string.IsNullOrWhiteSpace(analytics.ProfileToken) ? null : analytics.ProfileToken;

            foreach (var objectClass in classes)
            {
                options.AnalyticsTriggers.Add(objectClass);
            }

            // Motion is the default trigger, unless the camera's object detection decides instead.
            var topics = camera.Events?.TriggerTopics?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
            var defaultToMotion = topics.Count == 0 && classes.Count == 0 && camera.Mode == RecordingMode.Triggered;
            foreach (var topic in defaultToMotion ? CameraRecordingOptions.CommonMotionTopics.ToList() : topics)
            {
                options.EventTriggers.Add(topic);
            }

            return options;
        }

        internal static ICamera AddCamera(IRecordingManager manager, CameraOptions camera)
        {
            var options = ToRecordingOptions(camera);

            if (!string.IsNullOrWhiteSpace(camera.OnvifUrl))
            {
                var builder = CameraBuilder.GetBuilder().SetUrl(camera.OnvifUrl).SetMediaVersion(camera.MediaVersion);

                if (!string.IsNullOrWhiteSpace(camera.Username))
                {
                    builder.SetUsername(camera.Username);
                }

                if (!string.IsNullOrWhiteSpace(camera.Password))
                {
                    builder.SetPassword(camera.Password);
                }

                var onvifCamera = builder.Build();
                manager.AddCamera(camera.Id, onvifCamera, options);
                return onvifCamera;
            }

            var streamUri = new Uri(camera.RtspUrl);
            manager.AddCamera(camera.Id, ct =>
            {
                var builder = LiveVideoStreamBuilder.GetBuilder().SetStreamUri(streamUri).SetTransport(camera.Transport);

                if (!string.IsNullOrWhiteSpace(camera.Username))
                {
                    builder.SetCredentials(camera.Username, camera.Password);
                }

                return builder.BuildAsync(ct);
            }, options);

            return null;
        }

        private static ISegmentFormat ToFormat(string format)
        {
            switch ((format ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "mpegts":
                case "ts":
                    return SegmentFormats.MpegTs;
                case "elementarystream":
                case "raw":
                    return SegmentFormats.ElementaryStream;
                default:
                    throw new InvalidOperationException($"Unknown recording format '{format}'. Use MpegTs or ElementaryStream.");
            }
        }

        private static long ToBytes(double value, long unit)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException("Sizes must be greater than zero.");
            }

            return (long)(value * unit);
        }

        #endregion
    }

    /// <summary>
    /// The recording manager built from configuration, plus the ONVIF cameras for snapshots.
    /// </summary>
    public sealed class ConfiguredRecording
    {
        #region Constructor

        internal ConfiguredRecording(IRecordingManager manager, IReadOnlyDictionary<string, (ICamera Camera, string ProfileToken)> onvifCameras)
        {
            Manager = manager;
            _onvifCameras = new System.Collections.Concurrent.ConcurrentDictionary<string, (ICamera Camera, string ProfileToken)>(onvifCameras, StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region Private Variables

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ICamera Camera, string ProfileToken)> _onvifCameras;

        #endregion

        #region Public Variables

        public IRecordingManager Manager { get; }

        #endregion

        #region Public Methods

        /// <summary>Starts recording a camera (validated by the caller); disabled cameras are not added.</summary>
        public void AddCamera(CameraOptions camera)
        {
            if (camera == null || !camera.Enabled)
            {
                return;
            }

            var onvifCamera = RecordingConfiguration.AddCamera(Manager, camera);
            if (onvifCamera != null)
            {
                _onvifCameras[camera.Id] = (onvifCamera, string.IsNullOrWhiteSpace(camera.ProfileToken) ? null : camera.ProfileToken);
            }
        }

        /// <summary>Stops recording a camera (closing its open segment); false when it was not recorded.</summary>
        public Task<bool> RemoveCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            _onvifCameras.TryRemove(cameraId ?? string.Empty, out _);
            return Manager.RemoveCameraAsync(cameraId, cancellationToken);
        }

        public Core.Abstract.Interface.Ptz.ICameraPtz GetPtz(string cameraId)
        {
            if (cameraId == null || !_onvifCameras.TryGetValue(cameraId, out var camera))
            {
                throw new NotSupportedException($"Camera '{cameraId}' is not an ONVIF camera: PTZ is not available.");
            }

            return camera.Camera.InitializePtz();
        }

        public Core.Abstract.Interface.Imaging.ICameraImaging GetImaging(string cameraId)
        {
            if (cameraId == null || !_onvifCameras.TryGetValue(cameraId, out var camera))
            {
                throw new NotSupportedException($"Camera '{cameraId}' is not an ONVIF camera: imaging is not available.");
            }

            return camera.Camera.InitializeImaging();
        }

        public Core.Abstract.Interface.DeviceIo.ICameraIo GetIo(string cameraId)
        {
            if (cameraId == null || !_onvifCameras.TryGetValue(cameraId, out var camera))
            {
                throw new NotSupportedException($"Camera '{cameraId}' is not an ONVIF camera: relays and inputs are not available.");
            }

            return camera.Camera.InitializeIo();
        }

        public Core.Abstract.Interface.Replay.ICameraRecordings GetDeviceRecordings(string cameraId)
        {
            if (cameraId == null || !_onvifCameras.TryGetValue(cameraId, out var camera))
            {
                throw new NotSupportedException($"Camera '{cameraId}' is not an ONVIF camera: recordings on the device are not available.");
            }

            return camera.Camera.InitializeRecordings();
        }

        public Task<byte[]> GetSnapshotAsync(string cameraId, CancellationToken cancellationToken)
        {
            if (cameraId == null || !_onvifCameras.TryGetValue(cameraId, out var camera))
            {
                throw new NotSupportedException($"Camera '{cameraId}' is not an ONVIF camera: snapshots are not available.");
            }

            return camera.Camera.InitializeMedia().GetSnapshotAsync(camera.ProfileToken, cancellationToken);
        }

        #endregion
    }
}
