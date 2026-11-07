// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Recording;
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

            foreach (var camera in options.Cameras.Where(c => c.Enabled))
            {
                AddCamera(manager, camera);
            }

            return manager;
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

        private static void AddCamera(IRecordingManager manager, CameraOptions camera)
        {
            var rotation = ToRotationPolicy(camera.Rotation);

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

                manager.AddCamera(camera.Id, builder.Build(), string.IsNullOrWhiteSpace(camera.ProfileToken) ? null : camera.ProfileToken, camera.Transport, rotation);
                return;
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
            }, rotation);
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
}
