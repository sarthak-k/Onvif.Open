// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Alerts
{
    /// <summary>
    /// Raises alerts: each event that starts is checked against the event rules; every <see cref="Interval"/> the
    /// cameras that are offline are checked against the offline rules (once per outage and rule). A rule alerts at most
    /// once per camera in its cooldown. Alerts are saved to the inbox, then delivered (email, webhook); alerts older
    /// than <see cref="Retention"/> are removed.
    /// </summary>
    public sealed class AlertEngine : BackgroundService
    {
        #region Constructor

        public AlertEngine(EventRecorder events, IRecordingManager recording, AlertRuleCache rules, IAlertStore store, AlertDelivery delivery, CameraDirectory directory, ITenancyStore tenancy, ILogger<AlertEngine> logger, TimeProvider timeProvider = null)
        {
            _events = events;
            _recording = recording;
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _tenancy = tenancy ?? throw new ArgumentNullException(nameof(tenancy));
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _started = Channel.CreateBounded<EventRecord>(new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        }

        #endregion

        #region Public Variables

        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan Retention = TimeSpan.FromDays(90);

        #endregion

        #region Private Variables

        private static readonly TimeSpan ZoneLifetime = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

        private readonly EventRecorder _events;
        private readonly IRecordingManager _recording;
        private readonly AlertRuleCache _rules;
        private readonly IAlertStore _store;
        private readonly AlertDelivery _delivery;
        private readonly CameraDirectory _directory;
        private readonly ITenancyStore _tenancy;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly Channel<EventRecord> _started;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();

        /// <summary>Offline cameras: since when, and the rules that alerted for this outage.</summary>
        private readonly Dictionary<string, (DateTime Since, string EventId, HashSet<string> Alerted)> _offline = new Dictionary<string, (DateTime, string, HashSet<string>)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>When each rule last alerted for each camera ("rule|camera").</summary>
        private readonly Dictionary<string, DateTime> _lastAlert = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, (TimeZoneInfo Zone, DateTime Until)> _zones = new Dictionary<string, (TimeZoneInfo, DateTime)>(StringComparer.OrdinalIgnoreCase);
        private DateTime _nextCleanup;

        #endregion

        #region Public Methods

        /// <summary>Checks an event that started against the event rules (also used by tests); the alerts raised.</summary>
        public async Task<IReadOnlyList<AlertRecord>> HandleAsync(EventRecord record, CancellationToken cancellationToken = default)
        {
            if (record.Category == EventCategories.Offline && record.CameraId != null)
            {
                lock (_sync)
                {
                    if (!_offline.ContainsKey(record.CameraId))
                    {
                        _offline[record.CameraId] = (record.StartTime, record.Id, new HashSet<string>(StringComparer.Ordinal));
                    }
                }
            }

            var raised = new List<AlertRecord>();
            var rules = await _rules.GetAsync(cancellationToken).ConfigureAwait(false);
            var candidates = rules.Where(r => r.Kind == AlertKinds.Event && AlertMatcher.Watches(r, record.OrganizationId, record.SiteId, record.CameraId)).ToList();
            if (candidates.Count == 0)
            {
                return raised;
            }

            var siteTime = await SiteTimeAsync(record.SiteId, DateTime.SpecifyKind(record.StartTime, DateTimeKind.Utc), cancellationToken).ConfigureAwait(false);
            var camera = _directory.Find(record.CameraId);
            foreach (var rule in candidates.Where(r => AlertMatcher.Matches(r, record, siteTime)))
            {
                var alert = await RaiseAsync(rule, new AlertRecord
                {
                    SiteId = record.SiteId,
                    CameraId = record.CameraId,
                    CameraName = camera?.Name,
                    EventId = record.Id,
                    Category = record.Category,
                    Severity = record.Severity ?? Severities.Info,
                    Title = record.Title ?? record.Category,
                    TriggeredAt = record.StartTime
                }, cancellationToken).ConfigureAwait(false);
                if (alert != null)
                {
                    raised.Add(alert);
                }
            }

            return raised;
        }

        /// <summary>The camera records again: its outage is over.</summary>
        public void CameraRecovered(string cameraId)
        {
            lock (_sync)
            {
                _offline.Remove(cameraId ?? string.Empty);
            }
        }

        /// <summary>Checks the offline cameras against the offline rules once (also used by tests); the alerts raised.</summary>
        public async Task<IReadOnlyList<AlertRecord>> CheckOfflineAsync(CancellationToken cancellationToken = default)
        {
            var raised = new List<AlertRecord>();
            List<(string CameraId, DateTime Since, string EventId, HashSet<string> Alerted)> offline;
            lock (_sync)
            {
                offline = _offline.Select(o => (o.Key, o.Value.Since, o.Value.EventId, o.Value.Alerted)).ToList();
            }

            if (offline.Count == 0)
            {
                return raised;
            }

            var rules = (await _rules.GetAsync(cancellationToken).ConfigureAwait(false)).Where(r => r.Kind == AlertKinds.Offline).ToList();
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            foreach (var (cameraId, since, eventId, alerted) in offline)
            {
                var camera = _directory.Find(cameraId);
                if (camera == null)
                {
                    CameraRecovered(cameraId);
                    continue;
                }

                var siteTime = await SiteTimeAsync(camera.SiteId, now, cancellationToken).ConfigureAwait(false);
                foreach (var rule in rules.Where(r => !alerted.Contains(r.Id) && AlertMatcher.MatchesOffline(r, camera, now - since, siteTime)))
                {
                    lock (_sync)
                    {
                        alerted.Add(rule.Id);
                    }

                    var minutes = (int)Math.Round((now - since).TotalMinutes);
                    var alert = await RaiseAsync(rule, new AlertRecord
                    {
                        SiteId = camera.SiteId,
                        CameraId = camera.Id,
                        CameraName = camera.Name,
                        EventId = eventId,
                        Category = EventCategories.Offline,
                        Severity = Severities.Warning,
                        Title = $"Camera offline for {minutes} min",
                        TriggeredAt = now
                    }, cancellationToken).ConfigureAwait(false);
                    if (alert != null)
                    {
                        raised.Add(alert);
                    }
                }
            }

            return raised;
        }

        #endregion

        #region Protected Methods

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_events != null)
            {
                _events.Started += OnStarted;
            }

            if (_recording != null)
            {
                _recording.CameraStateChanged += OnStateChanged;
            }

            try
            {
                await Task.WhenAll(ReadEventsAsync(stoppingToken), TickAsync(stoppingToken)).ConfigureAwait(false);
            }
            finally
            {
                if (_events != null)
                {
                    _events.Started -= OnStarted;
                }

                if (_recording != null)
                {
                    _recording.CameraStateChanged -= OnStateChanged;
                }
            }
        }

        #endregion

        #region Private Methods

        private void OnStarted(EventRecord record) => _started.Writer.TryWrite(record);

        private void OnStateChanged(object sender, CameraStateChange change)
        {
            if (change.State == RecordingState.Recording)
            {
                CameraRecovered(change.CameraId);
            }
        }

        private async Task ReadEventsAsync(CancellationToken stoppingToken)
        {
            try
            {
                await foreach (var record in _started.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                {
                    try
                    {
                        await HandleAsync(record, stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger?.LogWarning(ex, "Could not check event {EventId} against the alert rules", record.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }

        private async Task TickAsync(CancellationToken stoppingToken)
        {
            using (var timer = new PeriodicTimer(Interval, _timeProvider))
            {
                try
                {
                    while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await CheckOfflineAsync(stoppingToken).ConfigureAwait(false);
                            var now = _timeProvider.GetUtcNow().UtcDateTime;
                            if (now >= _nextCleanup)
                            {
                                _nextCleanup = now + CleanupInterval;
                                await _store.DeleteAlertsBeforeAsync(now - Retention, stoppingToken).ConfigureAwait(false);
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger?.LogWarning(ex, "Could not check the offline alert rules");
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }
            }
        }

        /// <summary>Saves and delivers the alert unless the rule alerted for the camera in its cooldown; null then.</summary>
        private async Task<AlertRecord> RaiseAsync(AlertRule rule, AlertRecord alert, CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var key = $"{rule.Id}|{alert.CameraId ?? alert.SiteId}";
            lock (_sync)
            {
                if (_lastAlert.TryGetValue(key, out var last) && now - last < TimeSpan.FromMinutes(rule.CooldownMinutes))
                {
                    return null;
                }

                _lastAlert[key] = now;
                if (_lastAlert.Count > 10_000)
                {
                    var horizon = now - TimeSpan.FromMinutes(AlertService.MaxMinutes);
                    foreach (var stale in _lastAlert.Where(p => p.Value < horizon).Select(p => p.Key).ToList())
                    {
                        _lastAlert.Remove(stale);
                    }
                }
            }

            alert.Id = Guid.NewGuid().ToString("D");
            alert.OrganizationId = rule.OrganizationId;
            alert.RuleId = rule.Id;
            alert.RuleName = rule.Name;

            // One alert is saved and delivered at a time: a slow webhook delays alerts, it does not pile them up.
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _store.SaveAlertAsync(alert, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Alert: {Title} at {Camera} (rule {Rule})", alert.Title, alert.CameraName ?? alert.SiteId, rule.Name);
                if ((await _delivery.DeliverAsync(rule, alert, cancellationToken).ConfigureAwait(false)).Count > 0)
                {
                    await _store.SaveAlertAsync(alert, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }

            return alert;
        }

        private async Task<DateTime> SiteTimeAsync(string siteId, DateTime utc, CancellationToken cancellationToken)
        {
            var key = siteId ?? string.Empty;
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            TimeZoneInfo zone;
            lock (_sync)
            {
                zone = _zones.TryGetValue(key, out var cached) && cached.Until > now ? cached.Zone : null;
            }

            if (zone == null)
            {
                var id = siteId == null ? null : (await _tenancy.FindSiteAsync(siteId, cancellationToken).ConfigureAwait(false))?.TimeZone;
                zone = TimeZoneInfo.Utc;
                if (!string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found))
                {
                    zone = found;
                }

                lock (_sync)
                {
                    _zones[key] = (zone, now + ZoneLifetime);
                }
            }

            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        }

        #endregion
    }
}
