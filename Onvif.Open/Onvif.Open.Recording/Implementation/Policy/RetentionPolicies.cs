// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Recording.Implementation.Policy
{
    /// <summary>
    /// Built-in retention policies; the oldest segments are always removed first.
    /// Implement <see cref="IRetentionPolicy"/> for anything else.
    /// <code>RetentionPolicies.KeepFor(TimeSpan.FromDays(7)).And(RetentionPolicies.MinFreeSpace(10 * RotationPolicies.GiB))</code>
    /// </summary>
    public static class RetentionPolicies
    {
        #region Public Variables

        /// <summary>Default: never let the volume drop below 1 GiB of free space.</summary>
        public static IRetentionPolicy Default => MinFreeSpace(RotationPolicies.GiB);

        /// <summary>Never deletes anything.</summary>
        public static IRetentionPolicy KeepAll { get; } = new DelegateRetentionPolicy(_ => Enumerable.Empty<RecordingSegment>());

        #endregion

        #region Public Methods

        /// <summary>Deletes segments older than <paramref name="age"/> (by their last write time).</summary>
        public static IRetentionPolicy KeepFor(TimeSpan age)
        {
            if (age <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(age));
            }

            return new DelegateRetentionPolicy(c => c.Segments.Where(s => c.Now - s.LastWriteTime > age));
        }

        /// <summary>Keeps the recordings of all cameras together below <paramref name="bytes"/>.</summary>
        public static IRetentionPolicy MaxTotalSize(long bytes)
        {
            RequirePositive(bytes, nameof(bytes));
            return new DelegateRetentionPolicy(c => OldestBeyond(c.Segments, bytes));
        }

        /// <summary>Keeps the recordings of each camera below <paramref name="bytes"/>.</summary>
        public static IRetentionPolicy MaxSizePerCamera(long bytes)
        {
            RequirePositive(bytes, nameof(bytes));
            return new DelegateRetentionPolicy(c => c.Segments
                .GroupBy(s => s.CameraId, StringComparer.OrdinalIgnoreCase)
                .SelectMany(g => OldestBeyond(g.ToList(), bytes)));
        }

        /// <summary>Deletes the oldest segments until the volume has at least <paramref name="bytes"/> free.</summary>
        public static IRetentionPolicy MinFreeSpace(long bytes)
        {
            RequirePositive(bytes, nameof(bytes));
            return new DelegateRetentionPolicy(c => FreeUp(c, bytes));
        }

        /// <summary>Deletes a segment when any of <paramref name="policies"/> selects it.</summary>
        public static IRetentionPolicy All(params IRetentionPolicy[] policies)
        {
            if (policies == null || policies.Length == 0 || policies.Any(p => p == null))
            {
                throw new ArgumentException("At least one non-null retention policy is required.", nameof(policies));
            }

            return new DelegateRetentionPolicy(c => policies.SelectMany(p => p.SelectForDeletion(c)).Distinct());
        }

        public static IRetentionPolicy And(this IRetentionPolicy policy, IRetentionPolicy other)
        {
            return All(policy, other);
        }

        /// <summary>Wraps a custom rule.</summary>
        public static IRetentionPolicy When(Func<RetentionContext, IEnumerable<RecordingSegment>> selector)
        {
            return new DelegateRetentionPolicy(selector ?? throw new ArgumentNullException(nameof(selector)));
        }

        #endregion

        #region Private Methods

        private static void RequirePositive(long value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        /// <summary>Oldest segments that must go so the newest ones fit in <paramref name="limit"/>.</summary>
        private static IEnumerable<RecordingSegment> OldestBeyond(IReadOnlyList<RecordingSegment> segments, long limit)
        {
            var total = segments.Sum(s => s.Size);

            foreach (var segment in segments.OrderBy(s => s.StartTime))
            {
                if (total <= limit)
                {
                    yield break;
                }

                total -= segment.Size;
                yield return segment;
            }
        }

        private static IEnumerable<RecordingSegment> FreeUp(RetentionContext context, long required)
        {
            if (!context.AvailableFreeBytes.HasValue)
            {
                yield break;
            }

            var free = context.AvailableFreeBytes.Value;

            foreach (var segment in context.Segments.OrderBy(s => s.StartTime))
            {
                if (free >= required)
                {
                    yield break;
                }

                free += segment.Size;
                yield return segment;
            }
        }

        #endregion

        private sealed class DelegateRetentionPolicy : IRetentionPolicy
        {
            private readonly Func<RetentionContext, IEnumerable<RecordingSegment>> _selector;

            public DelegateRetentionPolicy(Func<RetentionContext, IEnumerable<RecordingSegment>> selector)
            {
                _selector = selector;
            }

            public IEnumerable<RecordingSegment> SelectForDeletion(RetentionContext context)
            {
                // Defensive: a custom policy must never be able to delete a segment that is being written.
                return _selector(context).Where(s => !s.IsActive);
            }
        }
    }
}
