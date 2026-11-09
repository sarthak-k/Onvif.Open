// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Recorder.Events
{
    /// <summary>
    /// When each camera had events, in memory, so retention can keep the video around them without a database query
    /// per file: per camera a sorted list of merged time spans (with a margin before and after each event), and the
    /// events still going on.
    /// </summary>
    public sealed class EventIndex
    {
        /// <summary>Video kept before and after an event, beyond its own duration.</summary>
        public static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

        private readonly Dictionary<string, List<(DateTime Start, DateTime End)>> _spans = new Dictionary<string, List<(DateTime, DateTime)>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Events still going on, per camera: their start times (they keep everything after them).</summary>
        private readonly Dictionary<string, List<DateTime>> _open = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();

        /// <summary>Records an event; one without an end is going on until <see cref="Close"/>.</summary>
        public void Add(string cameraId, DateTime start, DateTime? end)
        {
            if (cameraId == null)
            {
                return;
            }

            lock (_sync)
            {
                if (end.HasValue)
                {
                    AddClosed(cameraId, start - Margin, end.Value + Margin);
                }
                else
                {
                    if (!_open.TryGetValue(cameraId, out var open))
                    {
                        _open[cameraId] = open = new List<DateTime>();
                    }

                    open.Add(start);
                }
            }
        }

        /// <summary>An event that was going on ended.</summary>
        public void Close(string cameraId, DateTime start, DateTime end)
        {
            if (cameraId == null)
            {
                return;
            }

            lock (_sync)
            {
                if (_open.TryGetValue(cameraId, out var open))
                {
                    open.Remove(start);
                }

                AddClosed(cameraId, start - Margin, end + Margin);
            }
        }

        /// <summary>True when any event of the camera overlaps [from, to].</summary>
        public bool Overlaps(string cameraId, DateTimeOffset from, DateTimeOffset to)
        {
            var start = from.UtcDateTime;
            var end = to.UtcDateTime;

            lock (_sync)
            {
                if (_open.TryGetValue(cameraId ?? string.Empty, out var open) && open.Any(o => o - Margin <= end))
                {
                    return true;
                }

                if (!_spans.TryGetValue(cameraId ?? string.Empty, out var list))
                {
                    return false;
                }

                // Binary search for the last span starting at or before the end of the range; spans are merged and
                // sorted, so only that one can overlap.
                int low = 0, high = list.Count - 1, found = -1;
                while (low <= high)
                {
                    var middle = (low + high) / 2;
                    if (list[middle].Start <= end)
                    {
                        found = middle;
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle - 1;
                    }
                }

                return found >= 0 && list[found].End >= start;
            }
        }

        private void AddClosed(string cameraId, DateTime start, DateTime end)
        {
            if (!_spans.TryGetValue(cameraId, out var list))
            {
                _spans[cameraId] = list = new List<(DateTime, DateTime)>();
            }

            var index = list.FindIndex(s => s.Start > start);
            index = index < 0 ? list.Count : index;
            list.Insert(index, (start, end));
            Merge(list, Math.Max(0, index - 1));
        }

        /// <summary>Forgets spans that ended before <paramref name="before"/>.</summary>
        public void Prune(DateTime before)
        {
            lock (_sync)
            {
                foreach (var list in _spans.Values)
                {
                    list.RemoveAll(s => s.End < before);
                }
            }
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _spans.Values.Sum(l => l.Count);
                }
            }
        }

        private static void Merge(List<(DateTime Start, DateTime End)> list, int from)
        {
            for (var i = from; i < list.Count - 1;)
            {
                if (list[i + 1].Start <= list[i].End)
                {
                    list[i] = (list[i].Start, list[i].End > list[i + 1].End ? list[i].End : list[i + 1].End);
                    list.RemoveAt(i + 1);
                }
                else
                {
                    i++;
                }
            }
        }
    }
}
