// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Recorder.Configuration
{
    /// <summary>
    /// Chooses a camera's streams so nobody has to: the main stream (recorded, shown enlarged) is the sharpest video
    /// profile, H.264 before H.265 at the same size (it plays in every browser); the sub-stream (small tiles, snapshots)
    /// is the smallest other one. Motion JPEG only when there is nothing else.
    /// </summary>
    public static class StreamSelection
    {
        #region Public Methods

        public static StreamRecommendation Recommend(IReadOnlyList<ProbeProfile> profiles)
        {
            var video = (profiles ?? Array.Empty<ProbeProfile>()).Where(p => !string.IsNullOrEmpty(p.Token)).ToList();
            var preferred = video.Where(p => !IsJpeg(p.Encoding)).ToList();
            var candidates = preferred.Count > 0 ? preferred : video;

            if (candidates.Count == 0)
            {
                return new StreamRecommendation();
            }

            var main = candidates
                .OrderByDescending(Pixels)
                .ThenBy(p => CodecRank(p.Encoding))
                .ThenByDescending(p => p.FrameRate ?? 0)
                .First();

            var sub = candidates
                .Where(p => p.Token != main.Token && Pixels(p) < Pixels(main))
                .OrderBy(Pixels)
                .ThenBy(p => CodecRank(p.Encoding))
                .FirstOrDefault();

            return new StreamRecommendation { MainProfile = main.Token, SubProfile = sub?.Token };
        }

        #endregion

        #region Private Methods

        private static long Pixels(ProbeProfile profile) => (long)(profile.Width ?? 0) * (profile.Height ?? 0);

        private static bool IsJpeg(string encoding) => encoding != null && encoding.Contains("JPEG", StringComparison.OrdinalIgnoreCase);

        private static int CodecRank(string encoding)
        {
            if (encoding != null && (encoding.Contains("264", StringComparison.Ordinal) || encoding.Equals("H264", StringComparison.OrdinalIgnoreCase)))
            {
                return 0;
            }

            return encoding != null && (encoding.Contains("265", StringComparison.Ordinal) || encoding.Contains("HEVC", StringComparison.OrdinalIgnoreCase)) ? 1 : 2;
        }

        #endregion
    }

    public sealed class StreamRecommendation
    {
        /// <summary>The profile to record (null: the camera's first).</summary>
        public string MainProfile { get; set; }

        /// <summary>A smaller profile for tiles and snapshots, when the camera has one.</summary>
        public string SubProfile { get; set; }
    }
}
