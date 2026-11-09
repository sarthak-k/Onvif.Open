// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Onvif.Open.Recorder.Tenancy
{
    /// <summary>
    /// Weekly hours, e.g. Monday to Friday 08:00 to 18:00, in the site's time zone. A window whose end is not after
    /// its start runs past midnight (22:00 to 06:00); the day is the one it starts on.
    /// </summary>
    public sealed class ScheduleWindow
    {
        public List<DayOfWeek> Days { get; set; } = new List<DayOfWeek>();

        /// <summary>"HH:mm".</summary>
        public string Start { get; set; }

        /// <summary>"HH:mm"; "00:00" or a time before <see cref="Start"/>: the next day.</summary>
        public string End { get; set; }
    }

    /// <summary>
    /// When a "Schedule" policy records all the time; outside these hours it records around events only.
    /// </summary>
    public sealed class RecordingSchedule
    {
        #region Constructor

        private RecordingSchedule(IReadOnlyList<(HashSet<DayOfWeek> Days, TimeSpan Start, TimeSpan Length)> windows)
        {
            _windows = windows;
        }

        #endregion

        #region Public Variables

        public const int MaxWindows = 14;

        public static IReadOnlyList<ScheduleWindow> BusinessHours { get; } = new[]
        {
            new ScheduleWindow
            {
                Days = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                Start = "08:00",
                End = "18:00"
            }
        };

        #endregion

        #region Private Variables

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        private readonly IReadOnlyList<(HashSet<DayOfWeek> Days, TimeSpan Start, TimeSpan Length)> _windows;

        #endregion

        #region Public Methods

        /// <summary>Problems with the windows (empty when they are valid).</summary>
        public static List<string> Check(IReadOnlyList<ScheduleWindow> windows)
        {
            var errors = new List<string>();
            if (windows == null || windows.Count == 0)
            {
                errors.Add("A schedule needs at least one time range.");
                return errors;
            }

            if (windows.Count > MaxWindows)
            {
                errors.Add($"A schedule has at most {MaxWindows} time ranges.");
            }

            foreach (var window in windows)
            {
                if (window?.Days == null || window.Days.Count == 0 || window.Days.Any(d => !Enum.IsDefined(d)))
                {
                    errors.Add("Each time range needs at least one day.");
                }

                if (!TryParse(window?.Start, out var start) || !TryParse(window?.End, out var end))
                {
                    errors.Add($"Times are written as HH:mm, e.g. 08:00 ('{window?.Start}' to '{window?.End}').");
                }
                else if (start == end)
                {
                    errors.Add($"A time range cannot start and end at {window.Start}.");
                }
            }

            return errors;
        }

        /// <summary>The schedule of valid windows, or null when there are none.</summary>
        public static RecordingSchedule From(IReadOnlyList<ScheduleWindow> windows)
        {
            var parsed = (windows ?? Array.Empty<ScheduleWindow>())
                .Where(w => w?.Days?.Count > 0 && TryParse(w.Start, out _) && TryParse(w.End, out _))
                .Select(w =>
                {
                    TryParse(w.Start, out var start);
                    TryParse(w.End, out var end);
                    var length = end > start ? end - start : end + TimeSpan.FromDays(1) - start;
                    return (new HashSet<DayOfWeek>(w.Days), start, length);
                })
                .Where(w => w.length > TimeSpan.Zero)
                .ToList();

            return parsed.Count == 0 ? null : new RecordingSchedule(parsed);
        }

        public static List<ScheduleWindow> Deserialize(string json) =>
            string.IsNullOrEmpty(json) ? new List<ScheduleWindow>() : JsonSerializer.Deserialize<List<ScheduleWindow>>(json, Json) ?? new List<ScheduleWindow>();

        public static string Serialize(IReadOnlyList<ScheduleWindow> windows) =>
            windows == null || windows.Count == 0 ? null : JsonSerializer.Serialize(windows.Select(Normalize).ToList(), Json);

        /// <summary>How long the schedule stays on from <paramref name="local"/> (site time), or null when it is off.</summary>
        public TimeSpan? RemainingAt(DateTime local)
        {
            TimeSpan? remaining = null;
            foreach (var (days, start, length) in _windows)
            {
                // A window that started today, or yesterday and runs past midnight.
                for (var back = 0; back <= 1; back++)
                {
                    var day = local.Date.AddDays(-back);
                    if (!days.Contains(day.DayOfWeek))
                    {
                        continue;
                    }

                    var from = day + start;
                    var to = from + length;
                    if (local >= from && local < to && (remaining == null || to - local > remaining))
                    {
                        remaining = to - local;
                    }
                }
            }

            return remaining;
        }

        public bool IsOnAt(DateTime local) => RemainingAt(local).HasValue;

        #endregion

        #region Private Methods

        private static bool TryParse(string text, out TimeSpan time)
        {
            if (!string.IsNullOrWhiteSpace(text)
                && TimeSpan.TryParseExact(text.Trim(), new[] { @"hh\:mm", @"h\:mm" }, CultureInfo.InvariantCulture, out time)
                && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1))
            {
                return true;
            }

            time = default;
            return false;
        }

        private static ScheduleWindow Normalize(ScheduleWindow window)
        {
            TryParse(window.Start, out var start);
            TryParse(window.End, out var end);
            return new ScheduleWindow
            {
                Days = window.Days.Distinct().OrderBy(d => ((int)d + 6) % 7).ToList(),
                Start = start.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                End = end.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            };
        }

        #endregion
    }
}
