// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recording.Implementation.Policy;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class RetentionPolicyTest
    {
        #region Private Variables

        private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

        #endregion

        #region Private Methods

        private static RecordingSegment Segment(string camera, int hoursAgo, long size, bool active = false)
        {
            var start = Now.AddHours(-hoursAgo);
            return new RecordingSegment(camera, $"/r/{camera}/{hoursAgo}.ts", start, start.AddMinutes(10), size, active);
        }

        private static List<RecordingSegment> Segments()
        {
            return new List<RecordingSegment>
            {
                Segment("a", 50, 100),
                Segment("b", 40, 100),
                Segment("a", 30, 100),
                Segment("b", 20, 100),
                Segment("a", 10, 100)
            };
        }

        private static string[] Select(Onvif.Open.Core.Abstract.Interface.Recording.IRetentionPolicy policy, long? free = null, List<RecordingSegment> segments = null)
        {
            return policy.SelectForDeletion(new RetentionContext(segments ?? Segments(), Now, free)).Select(s => s.Path).ToArray();
        }

        #endregion

        #region Public Methods

        [Fact]
        public void KeepForShouldDeleteOlderSegments()
        {
            Assert.Equal(new[] { "/r/a/50.ts", "/r/b/40.ts" }, Select(RetentionPolicies.KeepFor(TimeSpan.FromHours(35))));
        }

        [Fact]
        public void MaxTotalSizeShouldDeleteOldestFirst()
        {
            Assert.Equal(new[] { "/r/a/50.ts", "/r/b/40.ts" }, Select(RetentionPolicies.MaxTotalSize(300)));
            Assert.Empty(Select(RetentionPolicies.MaxTotalSize(500)));
        }

        [Fact]
        public void MaxSizePerCameraShouldApplyToEachCamera()
        {
            Assert.Equal(new[] { "/r/a/50.ts" }, Select(RetentionPolicies.MaxSizePerCamera(200)));
            Assert.Equal(new[] { "/r/a/30.ts", "/r/a/50.ts", "/r/b/40.ts" }, Select(RetentionPolicies.MaxSizePerCamera(100)).OrderBy(p => p).ToArray());
        }

        [Fact]
        public void MinFreeSpaceShouldFreeEnoughSpace()
        {
            Assert.Equal(new[] { "/r/a/50.ts", "/r/b/40.ts" }, Select(RetentionPolicies.MinFreeSpace(250), free: 50));
            Assert.Empty(Select(RetentionPolicies.MinFreeSpace(250), free: 250));
            Assert.Empty(Select(RetentionPolicies.MinFreeSpace(250), free: null));
        }

        [Fact]
        public void AndShouldCombineWithoutDuplicates()
        {
            var policy = RetentionPolicies.KeepFor(TimeSpan.FromHours(45)).And(RetentionPolicies.MaxTotalSize(300));

            Assert.Equal(new[] { "/r/a/50.ts", "/r/b/40.ts" }, Select(policy));
        }

        [Fact]
        public void ActiveSegmentsShouldNeverBeSelected()
        {
            var segments = new List<RecordingSegment> { Segment("a", 50, 100, active: true), Segment("a", 40, 100) };

            Assert.Equal(new[] { "/r/a/40.ts" }, Select(RetentionPolicies.When(c => c.Segments), segments: segments));
        }

        [Fact]
        public void KeepAllShouldNeverDelete()
        {
            Assert.Empty(Select(RetentionPolicies.KeepAll, free: 0));
        }

        #endregion
    }
}
