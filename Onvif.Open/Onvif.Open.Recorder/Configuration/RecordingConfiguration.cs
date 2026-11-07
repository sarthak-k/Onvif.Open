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
using Onvif.Open.Core.Recording;
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

        public static IRecordingManager CreateManager(RecorderOptions options, ILoggerFactory loggerFactory)
        {
            return Create(options, loggerFactory).Manager;
        }

        /// <summary>Creates the manager and keeps the ONVIF cameras, which also provide snapshots.</summary>
        public static ConfiguredRecording Create(RecorderOptions options, ILoggerFactory loggerFactory)
        {
            Validate(options);

            var manager = RecordingManagerBuilder.GetBuilder()
                .SetStorageDirectory(options.StorageDirectory)
                .SetFormat(ToFormat(options.Format))
                .SetRotationPolicy(ToRotationPolicy(options.Rotation) ?? RotationPolicies.Default)
                .SetRetentionPolicy(ToRetentionPolicy(options.Retention))
                .SetRetentionInterval(options.RetentionInterval)
                .SetStallTimeout(options.StallTimeout)
                .SetReconnectDelay(options.MinReconnectDelay, options.MaxReconnectDelay)
                .SetLoggerFactory(loggerFactory)
                .Build();

            var onvifCameras = new Dictionary<string, (ICamera Camera, string ProfileToken)>(StringComparer.OrdinalIgnoreCase);

            foreach (var camera in options.Cameras.Where(c => c.Enabled))
            {
                var onvifCamera = AddCamera(manager, camera);
                if (onvifCamera != null)
                {
                    onvifCameras[camera.Id] = (onvifCamera, string.IsNullOrWhiteSpace(camera.ProfileToken) ? null : camera.ProfileToken);
                }
            }

            return new ConfiguredRecording(manager, onvifCameras);
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

            foreach (var camera in options.Cameras ?? new List<CameraOptions>())
            {
                if (!CameraIdentifier.IsValid(camera.Id))
                {
                    errors.Add($"Camera id '{camera.Id}' is invalid: use 1-64 letters, digits, '-' or '_'.");
                }

                if (string.IsNullOrWhiteSpace(camera.OnvifUrl) == string.IsNullOrWhiteSpace(camera.RtspUrl))
                {
                    errors.Add($"Camera '{camera.Id}' needs exactly one of OnvifUrl or RtspUrl.");
                }
            }

            foreach (var duplicate in (options.Cameras ?? new List<CameraOptions>()).GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                errors.Add($"Camera id '{duplicate.Key}' is used more than once.");
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
                SubscribeToEvents = camera.Events?.Enabled != false
            };

            var topics = camera.Events?.TriggerTopics?.Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? new List<string>();
            foreach (var topic in topics.Count > 0 || camera.Mode != RecordingMode.Triggered ? topics : CameraRecordingOptions.CommonMotionTopics.ToList())
            {
                options.EventTriggers.Add(topic);
            }

            return options;
        }

        private static ICamera AddCamera(IRecordingManager manager, CameraOptions camera)
        {
            var options = ToRecordingOptions(camera);

            if (!string.IsNullOrWhiteSpace(camera.OnvifUrl))
            {
                var builder = CameraBuilder.GetBuilder().SetUrl(camera.OnvifUrl);

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
            _onvifCameras = onvifCameras;
        }

        #endregion

        #region Private Variables

        private readonly IReadOnlyDictionary<string, (ICamera Camera, string ProfileToken)> _onvifCameras;

        #endregion

        #region Public Variables

        public IRecordingManager Manager { get; }

        #endregion

        #region Public Methods

        public Core.Abstract.Interface.Ptz.ICameraPtz GetPtz(string cameraId)
        {
            if (cameraId == null || !_onvifCameras.TryGetValue(cameraId, out var camera))
            {
                throw new NotSupportedException($"Camera '{cameraId}' is not an ONVIF camera: PTZ is not available.");
            }

            return camera.Camera.InitializePtz();
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
