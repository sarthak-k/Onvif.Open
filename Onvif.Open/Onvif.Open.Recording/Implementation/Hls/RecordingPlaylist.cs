// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recording.Implementation.Format;

namespace Onvif.Open.Recording.Implementation.Hls
{
    /// <summary>
    /// HLS playlist (VOD) over a camera's completed recordings, for playback in a browser. Local MPEG-TS segments are
    /// split into byte ranges at key frames (EXT-X-BYTERANGE), so playback starts after a few seconds of download
    /// instead of a whole segment; other segments are listed whole. Indexes of completed segments are cached.
    /// </summary>
    public sealed class RecordingPlaylist
    {
        #region Constructor

        public RecordingPlaylist(IRecordingManager manager, TimeSpan? fragmentDuration = null, int cacheSize = 1024)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _fragmentDuration = fragmentDuration ?? TimeSpan.FromSeconds(6);
            _cacheSize = cacheSize;
        }

        #endregion

        #region Private Variables

        private readonly IRecordingManager _manager;
        private readonly TimeSpan _fragmentDuration;
        private readonly int _cacheSize;
        private readonly ConcurrentDictionary<string, IReadOnlyList<Fragment>> _indexes = new ConcurrentDictionary<string, IReadOnlyList<Fragment>>(StringComparer.Ordinal);

        #endregion

        #region Public Methods

        /// <summary>
        /// The playlist of the completed segments overlapping <paramref name="from"/>..<paramref name="to"/>;
        /// <paramref name="segmentUri"/> gives the download URI of a segment (it must support HTTP range requests).
        /// </summary>
        public async Task<string> GetPlaylistAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, Func<RecordingSegment, string> segmentUri, CancellationToken cancellationToken = default)
        {
            var segments = _manager.GetSegments(cameraId)
                .Where(s => !s.IsActive && s.StartTime <= to && s.LastWriteTime >= from)
                .ToList();

            var entries = new List<(RecordingSegment Segment, IReadOnlyList<Fragment> Fragments)>();

            foreach (var segment in segments)
            {
                entries.Add((segment, await GetFragmentsAsync(segment, cancellationToken).ConfigureAwait(false)));
            }

            var builder = new StringBuilder("#EXTM3U\n#EXT-X-VERSION:4\n#EXT-X-PLAYLIST-TYPE:VOD\n");
            var target = entries.SelectMany(e => e.Fragments).Select(f => f.Duration).DefaultIfEmpty(_fragmentDuration).Max();
            builder.Append("#EXT-X-TARGETDURATION:").Append((int)Math.Ceiling(target.TotalSeconds)).Append('\n');

            for (var i = 0; i < entries.Count; i++)
            {
                var (segment, fragments) = entries[i];
                var uri = segmentUri(segment);

                // Every file starts its own timeline.
                if (i > 0)
                {
                    builder.Append("#EXT-X-DISCONTINUITY\n");
                }

                builder.Append("#EXT-X-PROGRAM-DATE-TIME:").Append(segment.StartTime.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)).Append('\n');

                foreach (var fragment in fragments)
                {
                    builder.Append("#EXTINF:").Append(fragment.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(",\n");

                    if (fragment.Length.HasValue)
                    {
                        builder.Append("#EXT-X-BYTERANGE:").Append(fragment.Length.Value).Append('@').Append(fragment.Offset).Append('\n');
                    }

                    builder.Append(uri).Append('\n');
                }
            }

            return builder.Append("#EXT-X-ENDLIST\n").ToString();
        }

        /// <summary>Byte ranges of a segment, each starting at the PAT before a key frame (internal for tests).</summary>
        internal static async Task<IReadOnlyList<Fragment>> IndexAsync(Stream input, long length, TimeSpan fragmentDuration, CancellationToken cancellationToken)
        {
            var starts = new List<(long Offset, long Pts)>();
            long? lastPts = null;

            await foreach (var frame in new MpegTsDemuxer().ReadAsync(input, cancellationToken).ConfigureAwait(false))
            {
                if (frame.IsAudio)
                {
                    continue;
                }

                if (frame.IsKeyFrame && (starts.Count == 0 || TimeSpan.FromTicks((frame.Pts - starts[starts.Count - 1].Pts) * TimeSpan.TicksPerSecond / 90000) >= fragmentDuration))
                {
                    starts.Add((starts.Count == 0 ? 0 : frame.Offset, frame.Pts));
                }

                lastPts = frame.Pts;
            }

            var fragments = new List<Fragment>();
            for (var i = 0; i < starts.Count; i++)
            {
                var end = i + 1 < starts.Count ? starts[i + 1].Offset : length;
                var endPts = i + 1 < starts.Count ? starts[i + 1].Pts : lastPts.Value + 3600;
                fragments.Add(new Fragment(starts[i].Offset, end - starts[i].Offset, TimeSpan.FromTicks((endPts - starts[i].Pts) * TimeSpan.TicksPerSecond / 90000)));
            }

            return fragments;
        }

        #endregion

        #region Private Methods

        private async Task<IReadOnlyList<Fragment>> GetFragmentsAsync(RecordingSegment segment, CancellationToken cancellationToken)
        {
            var whole = new[] { new Fragment(0, null, Max(segment.LastWriteTime - segment.StartTime, TimeSpan.FromSeconds(1))) };

            // Archived segments would have to be downloaded to be indexed: they are listed whole.
            if (!segment.IsLocal || !segment.FileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            {
                return whole;
            }

            var key = segment.Path + "|" + segment.Size;
            if (_indexes.TryGetValue(key, out var cached))
            {
                return cached;
            }

            IReadOnlyList<Fragment> fragments;
            try
            {
                using (var input = await _manager.OpenSegmentAsync(segment, cancellationToken).ConfigureAwait(false))
                {
                    fragments = input == null ? whole : await IndexAsync(input, segment.Size, _fragmentDuration, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (IOException)
            {
                return whole;
            }

            if (fragments.Count == 0)
            {
                // Not a readable MPEG-TS file.
                return whole;
            }

            if (_indexes.Count >= _cacheSize)
            {
                _indexes.Clear();
            }

            _indexes[key] = fragments;
            return fragments;
        }

        private static TimeSpan Max(TimeSpan value, TimeSpan minimum)
        {
            return value < minimum ? minimum : value;
        }

        #endregion

        /// <summary>A playable part of a segment; <see cref="Length"/> is null for the whole file.</summary>
        internal sealed class Fragment
        {
            public Fragment(long offset, long? length, TimeSpan duration)
            {
                Offset = offset;
                Length = length;
                Duration = duration;
            }

            public long Offset { get; }
            public long? Length { get; }
            public TimeSpan Duration { get; }
        }
    }
}
