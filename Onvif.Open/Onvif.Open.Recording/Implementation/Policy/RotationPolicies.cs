// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using Onvif.Open.Core.Abstract.Interface.Recording;

namespace Onvif.Open.Recording.Implementation.Policy
{
    /// <summary>
    /// Built-in rotation policies. Implement <see cref="IRotationPolicy"/> for anything else.
    /// <code>RotationPolicies.Every(TimeSpan.FromMinutes(10)).Or(RotationPolicies.MaxSize(512 * RotationPolicies.MiB))</code>
    /// </summary>
    public static class RotationPolicies
    {
        #region Public Variables

        public const long MiB = 1024L * 1024;
        public const long GiB = 1024L * MiB;

        /// <summary>Default: a new file every 10 minutes.</summary>
        public static IRotationPolicy Default => Every(TimeSpan.FromMinutes(10));

        #endregion

        #region Public Methods

        /// <summary>New file once the segment holds <paramref name="duration"/> of video.</summary>
        public static IRotationPolicy Every(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            return new DelegateRotationPolicy(s => s.Duration >= duration);
        }

        /// <summary>New file once the segment reaches <paramref name="bytes"/>.</summary>
        public static IRotationPolicy MaxSize(long bytes)
        {
            if (bytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bytes));
            }

            return new DelegateRotationPolicy(s => s.Size >= bytes);
        }

        /// <summary>
        /// New file whenever the wall clock crosses a multiple of <paramref name="interval"/> (UTC),
        /// e.g. <c>TimeSpan.FromHours(1)</c> gives one file per clock hour (10:00, 11:00, ...).
        /// </summary>
        public static IRotationPolicy AlignedToClock(TimeSpan interval)
        {
            if (interval <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            return new DelegateRotationPolicy(s =>
                s.Now.UtcTicks / interval.Ticks != s.StartedAt.UtcTicks / interval.Ticks);
        }

        /// <summary>Rotates as soon as any of <paramref name="policies"/> asks for it.</summary>
        public static IRotationPolicy Any(params IRotationPolicy[] policies)
        {
            if (policies == null || policies.Length == 0 || policies.Any(p => p == null))
            {
                throw new ArgumentException("At least one non-null rotation policy is required.", nameof(policies));
            }

            return new DelegateRotationPolicy(s => policies.Any(p => p.ShouldRotate(s)));
        }

        public static IRotationPolicy Or(this IRotationPolicy policy, IRotationPolicy other)
        {
            return Any(policy, other);
        }

        /// <summary>Wraps a custom rule, e.g. <c>RotationPolicies.When(s => s.FrameCount >= 9000)</c>.</summary>
        public static IRotationPolicy When(Func<ISegmentProgress, bool> predicate)
        {
            return new DelegateRotationPolicy(predicate ?? throw new ArgumentNullException(nameof(predicate)));
        }

        #endregion

        private sealed class DelegateRotationPolicy : IRotationPolicy
        {
            private readonly Func<ISegmentProgress, bool> _predicate;

            public DelegateRotationPolicy(Func<ISegmentProgress, bool> predicate)
            {
                _predicate = predicate;
            }

            public bool ShouldRotate(ISegmentProgress segment)
            {
                return _predicate(segment);
            }
        }
    }
}
