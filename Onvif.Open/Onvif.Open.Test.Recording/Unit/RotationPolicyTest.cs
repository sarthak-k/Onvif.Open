// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Recording.Implementation.Policy;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class RotationPolicyTest
    {
        #region Private Methods

        private sealed class Progress : ISegmentProgress
        {
            public string CameraId { get; set; } = "cam";
            public DateTimeOffset StartedAt { get; set; } = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
            public DateTimeOffset Now { get; set; } = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
            public TimeSpan Duration { get; set; }
            public long Size { get; set; }
            public int FrameCount { get; set; }
        }

        #endregion

        #region Public Methods

        [Fact]
        public void EveryShouldRotateOnceDurationIsReached()
        {
            var policy = RotationPolicies.Every(TimeSpan.FromMinutes(5));

            Assert.False(policy.ShouldRotate(new Progress { Duration = TimeSpan.FromSeconds(299) }));
            Assert.True(policy.ShouldRotate(new Progress { Duration = TimeSpan.FromMinutes(5) }));
        }

        [Fact]
        public void MaxSizeShouldRotateOnceSizeIsReached()
        {
            var policy = RotationPolicies.MaxSize(100 * RotationPolicies.MiB);

            Assert.False(policy.ShouldRotate(new Progress { Size = 100 * RotationPolicies.MiB - 1 }));
            Assert.True(policy.ShouldRotate(new Progress { Size = 100 * RotationPolicies.MiB }));
        }

        [Fact]
        public void AlignedToClockShouldRotateWhenCrossingBoundary()
        {
            var policy = RotationPolicies.AlignedToClock(TimeSpan.FromHours(1));
            var start = new DateTimeOffset(2026, 11, 7, 10, 59, 50, TimeSpan.Zero);

            Assert.False(policy.ShouldRotate(new Progress { StartedAt = start, Now = start.AddSeconds(9) }));
            Assert.True(policy.ShouldRotate(new Progress { StartedAt = start, Now = start.AddSeconds(11) }));
        }

        [Fact]
        public void OrShouldRotateWhenAnyPolicyRotates()
        {
            var policy = RotationPolicies.Every(TimeSpan.FromMinutes(10)).Or(RotationPolicies.MaxSize(1000));

            Assert.False(policy.ShouldRotate(new Progress { Duration = TimeSpan.FromMinutes(1), Size = 10 }));
            Assert.True(policy.ShouldRotate(new Progress { Duration = TimeSpan.FromMinutes(1), Size = 1000 }));
            Assert.True(policy.ShouldRotate(new Progress { Duration = TimeSpan.FromMinutes(10), Size = 10 }));
        }

        [Fact]
        public void WhenShouldUseCustomRule()
        {
            var policy = RotationPolicies.When(s => s.FrameCount >= 3);

            Assert.False(policy.ShouldRotate(new Progress { FrameCount = 2 }));
            Assert.True(policy.ShouldRotate(new Progress { FrameCount = 3 }));
        }

        [Fact]
        public void InvalidArgumentsShouldThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RotationPolicies.Every(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => RotationPolicies.MaxSize(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RotationPolicies.AlignedToClock(TimeSpan.FromSeconds(-1)));
            Assert.Throws<ArgumentException>(() => RotationPolicies.Any());
            Assert.Throws<ArgumentNullException>(() => RotationPolicies.When(null));
        }

        #endregion
    }
}
