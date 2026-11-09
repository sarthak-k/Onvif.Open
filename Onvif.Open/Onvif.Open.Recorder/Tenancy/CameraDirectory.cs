// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Tenancy
{
    /// <summary>Where a camera belongs and how long its video is kept.</summary>
    public sealed record CameraEntry(string Id, string OrganizationId, string SiteId, string PolicyId, string Name, TimeSpan Retention);

    /// <summary>
    /// The configured cameras in memory: who owns each (organization, site), its name and its retention. Answers the
    /// per-request access checks and the retention runs without a database query; kept current by every change.
    /// </summary>
    public sealed class CameraDirectory
    {
        private readonly ConcurrentDictionary<string, CameraEntry> _cameras = new ConcurrentDictionary<string, CameraEntry>(StringComparer.OrdinalIgnoreCase);

        public void Set(CameraOptions camera, string organizationId, RecordingPolicyRecord policy)
        {
            _cameras[camera.Id] = new CameraEntry(camera.Id, organizationId, camera.SiteId, camera.PolicyId, camera.Name ?? camera.Id, RecordingPolicyRules.RetentionOf(policy));
        }

        public void Remove(string cameraId)
        {
            _cameras.TryRemove(cameraId ?? string.Empty, out _);
        }

        public CameraEntry Find(string cameraId)
        {
            return cameraId != null && _cameras.TryGetValue(cameraId, out var entry) ? entry : null;
        }

        public IReadOnlyList<CameraEntry> All => _cameras.Values.ToList();

        /// <summary>How long the camera's video is kept; null for a camera this directory does not know.</summary>
        public TimeSpan? RetentionFor(string cameraId)
        {
            return Find(cameraId)?.Retention;
        }
    }

    /// <summary>
    /// The rules of a recording policy, applied to a camera's recording options.
    /// </summary>
    public static class RecordingPolicyRules
    {
        /// <summary>Recording mode and pre-/post-roll come from the policy.</summary>
        public static CameraOptions Apply(CameraOptions camera, RecordingPolicyRecord policy)
        {
            camera.Mode = policy.Mode == RecordingModes.EventsOnly ? Core.Recording.RecordingMode.Triggered : Core.Recording.RecordingMode.Continuous;
            camera.PreRoll = TimeSpan.FromSeconds(policy.PreRollSeconds);
            camera.PostRoll = TimeSpan.FromSeconds(policy.PostRollSeconds);
            return camera;
        }

        /// <summary>
        /// What a camera keeps: everything a camera recording only around events records is event video; continuous
        /// recordings keep their continuous retention (video around events is kept longer once events are stored).
        /// </summary>
        public static TimeSpan RetentionOf(RecordingPolicyRecord policy)
        {
            var days = policy.Mode == RecordingModes.EventsOnly ? policy.EventRetentionDays : policy.ContinuousRetentionDays;
            return TimeSpan.FromDays(Math.Max(1, days));
        }
    }
}
