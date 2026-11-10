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
    /// <param name="EventsOnly">Records only around events: everything it records is event video.</param>
    /// <param name="ContinuousRetention">How long video without events is kept.</param>
    /// <param name="EventRetention">How long video around events (and the events themselves) is kept.</param>
    /// <param name="Schedule">When it records all the time ("Schedule" policies); null otherwise.</param>
    /// <param name="IsOnvif">An ONVIF camera (it may pan, tilt and zoom); false for plain RTSP streams.</param>
    /// <param name="GatewayId">The gateway that records it (this recorder's, or another one reached through its tunnel).</param>
    public sealed record CameraEntry(string Id, string OrganizationId, string SiteId, string PolicyId, string Name, bool EventsOnly, TimeSpan ContinuousRetention, TimeSpan EventRetention, RecordingSchedule Schedule = null, bool IsOnvif = false, string GatewayId = null);

    /// <summary>
    /// The configured cameras in memory: who owns each (organization, site), its name and its retention. Answers the
    /// per-request access checks and the retention runs without a database query; kept current by every change.
    /// </summary>
    public sealed class CameraDirectory
    {
        private readonly ConcurrentDictionary<string, CameraEntry> _cameras = new ConcurrentDictionary<string, CameraEntry>(StringComparer.OrdinalIgnoreCase);

        public void Set(CameraOptions camera, string organizationId, RecordingPolicyRecord policy)
        {
            _cameras[camera.Id] = new CameraEntry(
                camera.Id, organizationId, camera.SiteId, camera.PolicyId, camera.Name ?? camera.Id,
                policy.Mode == RecordingModes.EventsOnly,
                TimeSpan.FromDays(Math.Max(1, policy.ContinuousRetentionDays)),
                TimeSpan.FromDays(Math.Max(1, policy.EventRetentionDays)),
                policy.Mode == RecordingModes.Schedule ? RecordingSchedule.From(RecordingSchedule.Deserialize(policy.Schedule)) : null,
                !string.IsNullOrWhiteSpace(camera.OnvifUrl),
                camera.GatewayId);
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

        /// <summary>How long video around events is kept, the longest of all cameras (for loading and pruning events).</summary>
        public TimeSpan LongestEventRetention => _cameras.Values.Select(c => c.EventRetention).DefaultIfEmpty(TimeSpan.FromDays(30)).Max();
    }

    /// <summary>
    /// The rules of a recording policy, applied to a camera's recording options.
    /// </summary>
    public static class RecordingPolicyRules
    {
        /// <summary>
        /// Recording mode and pre-/post-roll come from the policy. A schedule records in triggered mode: during its hours
        /// the recorder keeps the camera triggered (see Hosting.ScheduledRecording).
        /// </summary>
        public static CameraOptions Apply(CameraOptions camera, RecordingPolicyRecord policy)
        {
            camera.Mode = policy.Mode == RecordingModes.Always ? Core.Recording.RecordingMode.Continuous : Core.Recording.RecordingMode.Triggered;
            camera.PreRoll = TimeSpan.FromSeconds(policy.PreRollSeconds);
            camera.PostRoll = TimeSpan.FromSeconds(policy.PostRollSeconds);
            return camera;
        }

    }
}
