// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;
using Onvif.Open.Recording.Implementation.Policy;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class CameraRetentionTest
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        private static RecordingSegment Segment(string camera, double daysOld) =>
            new RecordingSegment(camera, $"/{camera}/{daysOld}.ts", Now.AddDays(-daysOld).AddMinutes(-10), Now.AddDays(-daysOld), 100, false);

        private static RecordingPolicyRecord Policy(string mode, int continuous = 7, int events = 30) =>
            new RecordingPolicyRecord { Id = mode, Mode = mode, ContinuousRetentionDays = continuous, EventRetentionDays = events };

        [Fact]
        public void EachCameraShouldKeepWhatItsPolicySays()
        {
            var directory = new CameraDirectory();
            directory.Set(new CameraOptions { Id = "always", SiteId = "s" }, "org", Policy(RecordingModes.Always));
            directory.Set(new CameraOptions { Id = "events", SiteId = "s" }, "org", Policy(RecordingModes.EventsOnly));

            var policy = RecordingConfiguration.WithCameraRetention(RetentionPolicies.KeepAll, directory.RetentionFor);
            var segments = new List<RecordingSegment> { Segment("always", 6), Segment("always", 8), Segment("events", 8), Segment("events", 31), Segment("unknown", 400) };

            var deleted = policy.SelectForDeletion(new RetentionContext(segments, Now, null)).Select(s => s.Path).ToList();

            // Continuous video 7 days; a camera recording only around events keeps its video 30 days; unknown cameras untouched.
            Assert.Equal(new[] { "/always/8.ts", "/events/31.ts" }, deleted.OrderBy(p => p));
        }

        [Fact]
        public void ChangedPoliciesShouldApplyAtTheNextRun()
        {
            var directory = new CameraDirectory();
            directory.Set(new CameraOptions { Id = "cam" }, "org", Policy(RecordingModes.Always, continuous: 30, events: 30));
            var policy = RecordingConfiguration.WithCameraRetention(RetentionPolicies.KeepAll, directory.RetentionFor);
            var context = new RetentionContext(new[] { Segment("cam", 10) }, Now, null);

            Assert.Empty(policy.SelectForDeletion(context));

            directory.Set(new CameraOptions { Id = "cam" }, "org", Policy(RecordingModes.Always, continuous: 7));
            Assert.Single(policy.SelectForDeletion(context));
        }

        [Fact]
        public void PoliciesShouldSetTheRecordingMode()
        {
            var camera = RecordingPolicyRules.Apply(new CameraOptions(), new RecordingPolicyRecord { Mode = RecordingModes.EventsOnly, PreRollSeconds = 3, PostRollSeconds = 20 });

            Assert.Equal(RecordingMode.Triggered, camera.Mode);
            Assert.Equal(TimeSpan.FromSeconds(3), camera.PreRoll);
            Assert.Equal(TimeSpan.FromSeconds(20), camera.PostRoll);
            Assert.Equal(RecordingMode.Continuous, RecordingPolicyRules.Apply(camera, new RecordingPolicyRecord { Mode = RecordingModes.Always }).Mode);
        }
    }
}
