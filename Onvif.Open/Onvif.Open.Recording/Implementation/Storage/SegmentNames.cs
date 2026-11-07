// Sarthak Killedar
// 2026-11-07

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Recording.Implementation.Storage
{
    /// <summary>
    /// Segment naming shared by the local storage and the archive:
    /// <c>{cameraId}/{yyyy-MM-dd}/{cameraId}_{yyyyMMddTHHmmssfffZ}[-n]{extension}</c> (UTC).
    /// Everything about a segment is derived from its name, so nothing else has to be persisted.
    /// </summary>
    internal static class SegmentNames
    {
        #region Private Variables

        private const string TimestampFormat = "yyyyMMdd'T'HHmmssfff'Z'";
        private const string DayFormat = "yyyy-MM-dd";

        private static readonly Regex SafeKey = new Regex("^[A-Za-z0-9_-][A-Za-z0-9_./-]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public Methods

        public static string Day(DateTimeOffset startTime)
        {
            return startTime.ToUniversalTime().ToString(DayFormat, CultureInfo.InvariantCulture);
        }

        public static string BaseName(string cameraId, DateTimeOffset startTime)
        {
            return $"{cameraId}_{startTime.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture)}";
        }

        /// <summary>Parses <c>{cameraId}_{timestamp}[-n]{extension}</c>.</summary>
        public static bool TryParseFileName(string cameraId, string fileName, out DateTimeOffset startTime, out int sequence)
        {
            startTime = default;
            sequence = 0;

            if (fileName == null || !fileName.StartsWith(cameraId + "_", StringComparison.Ordinal))
            {
                return false;
            }

            var name = System.IO.Path.GetFileNameWithoutExtension(fileName).Substring(cameraId.Length + 1);
            var dash = name.IndexOf('-');
            var timestamp = dash < 0 ? name : name.Substring(0, dash);

            if (dash >= 0 && !int.TryParse(name.Substring(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out sequence))
            {
                return false;
            }

            return DateTimeOffset.TryParseExact(timestamp, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out startTime);
        }

        /// <summary>The archive key of a segment, without the archive prefix.</summary>
        public static string ArchiveKey(RecordingSegment segment)
        {
            return $"{segment.CameraId}/{Day(segment.StartTime)}/{segment.FileName}";
        }

        /// <summary>Parses an archive key (without prefix) produced by <see cref="ArchiveKey"/>.</summary>
        public static bool TryParseArchiveKey(string key, out string cameraId, out string fileName, out DateTimeOffset startTime, out int sequence)
        {
            cameraId = null;
            fileName = null;
            startTime = default;
            sequence = 0;

            var parts = key?.Split('/');
            if (parts == null || parts.Length != 3 || !CameraIdentifier.IsValid(parts[0]))
            {
                return false;
            }

            cameraId = parts[0];
            fileName = parts[2];
            return TryParseFileName(cameraId, fileName, out startTime, out sequence);
        }

        /// <summary>True for keys an <see cref="Core.Abstract.Interface.Storage.ISegmentStore"/> is promised: no "..", no leading '/'.</summary>
        public static bool IsSafeKey(string key)
        {
            return key != null && SafeKey.IsMatch(key) && !key.Contains("..", StringComparison.Ordinal) && !key.Contains("//", StringComparison.Ordinal);
        }

        /// <summary>Sequence number of a segment ("-n" suffix) for ordering segments started in the same millisecond.</summary>
        public static int Sequence(RecordingSegment segment)
        {
            return TryParseFileName(segment.CameraId, segment.FileName, out _, out var sequence) ? sequence : 0;
        }

        #endregion
    }
}
