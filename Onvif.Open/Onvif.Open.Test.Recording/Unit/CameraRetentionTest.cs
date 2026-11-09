// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;
using Onvif.Open.Recording.Implementation.Policy;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class CameraRetentionTest
    {
        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        /// <summary>A 10-minute file that ended <paramref name="daysOld"/> days ago.</summary>
        private static RecordingSegment Segment(string camera, double daysOld) =>
            new RecordingSegment(camera, $"/{camera}/{daysOld}.ts", Now.AddDays(-daysOld).AddMinutes(-10), Now.AddDays(-daysOld), 100, false);

        private static RecordingPolicyRecord Policy(string mode, int continuous = 7, int events = 30) =>
            new RecordingPolicyRecord { Id = mode, Mode = mode, ContinuousRetentionDays = continuous, EventRetentionDays = events };

        private static (PolicyRetention Retention, CameraDirectory Directory, EventIndex Events) Create()
        {
            var directory = new CameraDirectory();
            directory.Set(new CameraOptions { Id = "always", SiteId = "s" }, "org", Policy(RecordingModes.Always));
            directory.Set(new CameraOptions { Id = "events", SiteId = "s" }, "org", Policy(RecordingModes.EventsOnly));
            var events = new EventIndex();
            return (new PolicyRetention(directory, events), directory, events);
        }

        private static List<string> Deleted(PolicyRetention retention, params RecordingSegment[] segments)
        {
            var policy = RecordingConfiguration.WithCameraRetention(RetentionPolicies.KeepAll, retention.IsExpired);
            return policy.SelectForDeletion(new RetentionContext(segments, Now, null)).Select(s => s.Path).OrderBy(p => p).ToList();
        }

        [Fact]
        public void ContinuousVideoShouldBeKeptSevenDaysAndVideoAroundEventsThirty()
        {
            var (retention, _, events) = Create();

            // A person in view eight days ago, during the file that ended then.
            var detected = Now.AddDays(-8).AddMinutes(-5).UtcDateTime;
            events.Add("always", detected, detected.AddSeconds(40));

            var deleted = Deleted(retention, Segment("always", 6), Segment("always", 8), Segment("always", 9), Segment("always", 31));

            Assert.Equal(new[] { "/always/31.ts", "/always/9.ts" }, deleted);
        }

        [Fact]
        public void CamerasRecordingOnlyAroundEventsShouldKeepEverythingForTheEventRetention()
        {
            var (retention, _, _) = Create();
            Assert.Equal(new[] { "/events/31.ts" }, Deleted(retention, Segment("events", 8), Segment("events", 29), Segment("events", 31)));
        }

        [Fact]
        public void UnknownCamerasShouldBeLeftToTheRecorderWideLimits()
        {
            var (retention, _, _) = Create();
            Assert.Empty(Deleted(retention, Segment("removed", 400)));
        }

        [Fact]
        public void ChangedPoliciesShouldApplyAtTheNextRun()
        {
            var (retention, directory, _) = Create();
            Assert.Empty(Deleted(retention, Segment("always", 5)));

            directory.Set(new CameraOptions { Id = "always", SiteId = "s" }, "org", Policy(RecordingModes.Always, continuous: 3, events: 30));
            Assert.Single(Deleted(retention, Segment("always", 5)));
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

    public class EventIndexTest
    {
        private static readonly DateTime T = new DateTime(2026, 11, 9, 2, 0, 0, DateTimeKind.Utc);

        private static bool Overlaps(EventIndex index, string camera, DateTime from, DateTime to) =>
            index.Overlaps(camera, new DateTimeOffset(from), new DateTimeOffset(to));

        [Fact]
        public void EventsShouldCoverTheirSpanWithAMargin()
        {
            var index = new EventIndex();
            index.Add("cam", T, T.AddSeconds(10));

            Assert.True(Overlaps(index, "cam", T.AddSeconds(5), T.AddSeconds(6)));
            Assert.True(Overlaps(index, "cam", T.AddSeconds(-29), T.AddSeconds(-28)), "the margin before");
            Assert.True(Overlaps(index, "cam", T.AddSeconds(39), T.AddSeconds(45)), "the margin after");
            Assert.False(Overlaps(index, "cam", T.AddMinutes(2), T.AddMinutes(3)));
            Assert.False(Overlaps(index, "other", T, T.AddSeconds(5)));
        }

        [Fact]
        public void EventsGoingOnShouldKeepEverythingAfterTheirStartUntilTheyEnd()
        {
            var index = new EventIndex();
            index.Add("cam", T, null);
            Assert.True(Overlaps(index, "cam", T.AddHours(5), T.AddHours(6)));

            index.Close("cam", T, T.AddMinutes(1));
            Assert.False(Overlaps(index, "cam", T.AddHours(5), T.AddHours(6)));
            Assert.True(Overlaps(index, "cam", T.AddSeconds(30), T.AddSeconds(31)));
        }

        [Fact]
        public void ManyEventsShouldMergeAndBePrunedWhenOld()
        {
            var index = new EventIndex();
            for (var i = 0; i < 100; i++)
            {
                // Out of order, overlapping: merged into one span per minute of activity.
                index.Add("cam", T.AddSeconds((i * 37) % 600), T.AddSeconds(((i * 37) % 600) + 5));
            }

            Assert.Equal(1, index.Count);
            Assert.True(Overlaps(index, "cam", T.AddMinutes(5), T.AddMinutes(5)));

            index.Prune(T.AddHours(1));
            Assert.Equal(0, index.Count);
        }
    }
}
