// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Hosting
{
    /// <summary>
    /// Records "Schedule" policy cameras all the time during their hours, in their site's time zone: every
    /// <see cref="Interval"/> it keeps them triggered until the next check (or the end of the hours, whichever is
    /// first). Outside their hours they record around events like "Events only" cameras.
    /// </summary>
    public sealed class ScheduledRecording : BackgroundService
    {
        #region Constructor

        public ScheduledRecording(CameraDirectory directory, ITenancyStore tenancy, IRecordingManager recording, TimeProvider timeProvider, ILogger<ScheduledRecording> logger)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _tenancy = tenancy ?? throw new ArgumentNullException(nameof(tenancy));
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _timeProvider = timeProvider ?? TimeProvider.System;
            _logger = logger;
        }

        #endregion

        #region Public Variables

        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

        /// <summary>Each check triggers this long, so a late check never leaves a gap.</summary>
        public static readonly TimeSpan Hold = TimeSpan.FromSeconds(90);

        #endregion

        #region Private Variables

        private readonly CameraDirectory _directory;
        private readonly ITenancyStore _tenancy;
        private readonly IRecordingManager _recording;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly HashSet<string> _recordingNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public Methods

        /// <summary>How long a camera on this schedule records from <paramref name="utcNow"/> on; null: its hours are over.</summary>
        public static TimeSpan? TriggerFor(RecordingSchedule schedule, TimeZoneInfo timeZone, DateTimeOffset utcNow)
        {
            var local = TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime;
            var remaining = schedule?.RemainingAt(local);
            return remaining is TimeSpan left ? (left < Hold ? left : Hold) : null;
        }

        /// <summary>Checks every scheduled camera once (also used by tests).</summary>
        public async Task CheckAsync(CancellationToken cancellationToken = default)
        {
            var scheduled = _directory.All.Where(c => c.Schedule != null).ToList();
            var timeZones = new Dictionary<string, TimeZoneInfo>(StringComparer.OrdinalIgnoreCase);
            var now = _timeProvider.GetUtcNow();

            foreach (var camera in scheduled)
            {
                if (!timeZones.TryGetValue(camera.SiteId ?? string.Empty, out var zone))
                {
                    zone = FindZone((await _tenancy.FindSiteAsync(camera.SiteId, cancellationToken).ConfigureAwait(false))?.TimeZone);
                    timeZones[camera.SiteId ?? string.Empty] = zone;
                }

                var duration = TriggerFor(camera.Schedule, zone, now);
                if (duration is TimeSpan hold)
                {
                    try
                    {
                        _recording.TriggerRecording(camera.Id, hold);
                        if (_recordingNow.Add(camera.Id))
                        {
                            _logger?.LogInformation("Camera {CameraId}: scheduled hours started, recording all the time", camera.Id);
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is KeyNotFoundException)
                    {
                        // A disabled or just removed camera: nothing to record.
                    }
                }
                else if (_recordingNow.Remove(camera.Id))
                {
                    _logger?.LogInformation("Camera {CameraId}: scheduled hours over, recording around events", camera.Id);
                }
            }

            _recordingNow.RemoveWhere(id => scheduled.All(c => !string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)));
        }

        #endregion

        #region Protected Methods

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (var timer = new PeriodicTimer(Interval, _timeProvider))
            {
                do
                {
                    try
                    {
                        await CheckAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Could not apply the recording schedules");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
            }
        }

        #endregion

        #region Private Methods

        private static TimeZoneInfo FindZone(string id)
        {
            try
            {
                return string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        #endregion
    }
}
