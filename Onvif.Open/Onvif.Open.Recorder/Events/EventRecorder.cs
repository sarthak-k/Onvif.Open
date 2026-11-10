// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Events
{
    /// <summary>
    /// Turns what cameras, detections, recording and plugins report into stored events:
    /// <list type="bullet">
    /// <item>ONVIF events with a state (motion, tampering, inputs) start an event when they turn on and end it when they turn off;</item>
    /// <item>detected objects are merged per camera and category ("a person was in view from 02:13:40 to 02:14:22");</item>
    /// <item>a camera that stops delivering video is "offline" until it records again;</item>
    /// <item>plugin events are stored as they are.</item>
    /// </list>
    /// The engine's threads only queue; one background loop writes in batches, keeps the event index current for
    /// retention, and removes events older than their camera's policy keeps them.
    /// </summary>
    public sealed class EventRecorder : BackgroundService
    {
        #region Constructor

        public EventRecorder(IRecordingManager manager, IPluginHost plugins, IEventStore store, CameraDirectory directory, EventIndex index, ILogger<EventRecorder> logger, TimeProvider timeProvider = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _plugins = plugins;
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _queue = Channel.CreateBounded<Operation>(new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true }, _ => Interlocked.Increment(ref _dropped));
        }

        #endregion

        #region Private Variables

        private const int QueueCapacity = 20_000;
        private const int BatchSize = 500;

        /// <summary>An object not seen for this long has left the view.</summary>
        public static readonly TimeSpan DetectionGap = TimeSpan.FromSeconds(5);

        /// <summary>Detections below this likelihood are not events.</summary>
        public const double MinDetectionLikelihood = 0.5;

        /// <summary>Events about no camera (some plugin events) or a removed one are kept this long.</summary>
        public static readonly TimeSpan CameralessRetention = TimeSpan.FromDays(365);

        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

        private readonly IRecordingManager _manager;
        private readonly IPluginHost _plugins;
        private readonly IEventStore _store;
        private readonly CameraDirectory _directory;
        private readonly EventIndex _index;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly Channel<Operation> _queue;
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();

        /// <summary>Events going on, by camera + source + topic or category.</summary>
        private readonly Dictionary<string, OpenEvent> _open = new Dictionary<string, OpenEvent>(StringComparer.Ordinal);
        private long _dropped;

        private sealed record OpenEvent(string Id, string CameraId, DateTime Start)
        {
            public DateTime LastSeen { get; set; }
        }

        private sealed record Operation(EventRecord Add, (string Id, string CameraId, DateTime Start, DateTime End)? Close);

        #endregion

        #region Public Variables

        /// <summary>Events lost because the database could not keep up (the queue was full).</summary>
        public long Dropped => Interlocked.Read(ref _dropped);

        #endregion

        #region Protected Methods

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await LoadIndexAsync(stoppingToken).ConfigureAwait(false);

            _manager.CameraEventReceived += OnCameraEvent;
            _manager.AnalyticsReceived += OnAnalytics;
            _manager.CameraStateChanged += OnStateChanged;
            if (_plugins != null)
            {
                _plugins.EventPublished += OnPluginEvent;
            }

            var nextCleanup = _timeProvider.GetUtcNow();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using (var tick = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                    {
                        tick.CancelAfter(TimeSpan.FromSeconds(1));
                        try
                        {
                            await _queue.Reader.WaitToReadAsync(tick.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                        {
                            // A second without news: time to end detections that left the view.
                        }
                    }

                    EndQuietDetections();
                    await FlushAsync(stoppingToken).ConfigureAwait(false);

                    if (_timeProvider.GetUtcNow() >= nextCleanup)
                    {
                        nextCleanup = _timeProvider.GetUtcNow() + CleanupInterval;
                        await CleanupAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                _manager.CameraEventReceived -= OnCameraEvent;
                _manager.AnalyticsReceived -= OnAnalytics;
                _manager.CameraStateChanged -= OnStateChanged;
                if (_plugins != null)
                {
                    _plugins.EventPublished -= OnPluginEvent;
                }

                // What was going on ends with the recorder: its end is not known later.
                EndAll(_timeProvider.GetUtcNow().UtcDateTime);
                await FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        #endregion

        #region Internal Methods (event sources)

        internal void OnCameraEvent(object sender, CameraEvent cameraEvent)
        {
            var camera = _directory.Find(cameraEvent.CameraId);
            if (camera == null)
            {
                return;
            }

            var state = cameraEvent.GetState();
            var time = cameraEvent.UtcTime.UtcDateTime;
            var key = $"{camera.Id}|camera|{cameraEvent.TopicPath}";
            var category = EventCategories.OfTopic(cameraEvent.TopicPath);

            // The state at subscription time: only an ongoing one is news.
            if (string.Equals(cameraEvent.PropertyOperation, "Initialized", StringComparison.OrdinalIgnoreCase) && state != true)
            {
                return;
            }

            if (state == false || string.Equals(cameraEvent.PropertyOperation, "Deleted", StringComparison.OrdinalIgnoreCase))
            {
                End(key, time);
                return;
            }

            var record = NewEvent(camera, category, category == EventCategories.Tamper ? "Warning" : "Info", "camera", EventCategories.TitleOfTopic(cameraEvent.TopicPath), cameraEvent.Topic, time, cameraEvent.Data.Count == 0 ? null : JsonSerializer.Serialize(cameraEvent.Data));

            if (state == true)
            {
                Begin(key, record);
            }
            else
            {
                // No state: something that happened at one moment.
                record.EndTime = time;
                Queue(new Operation(record, null));
                Publish(record);
            }
        }

        internal void OnAnalytics(object sender, AnalyticsFrame frame)
        {
            var camera = _directory.Find(frame.CameraId);
            if (camera == null)
            {
                return;
            }

            var time = frame.UtcTime.UtcDateTime;
            var seen = frame.Objects
                .Where(o => o.Class != null && (o.Likelihood ?? 1) >= MinDetectionLikelihood)
                .GroupBy(o => EventCategories.OfObjectClass(o.Class));

            foreach (var group in seen)
            {
                var key = $"{camera.Id}|detection|{group.Key}";

                OpenEvent ended = null;
                lock (_sync)
                {
                    if (_open.TryGetValue(key, out var open))
                    {
                        if (time - open.LastSeen <= DetectionGap)
                        {
                            open.LastSeen = time > open.LastSeen ? time : open.LastSeen;
                            continue;
                        }

                        ended = open;
                    }
                }

                // Seen again after a gap: a new event.
                if (ended != null)
                {
                    End(key, ended.LastSeen);
                }

                var classes = group.Select(o => o.Class).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var record = NewEvent(camera, group.Key, "Info", "detection", Capitalize(group.Key), string.Join(", ", classes), time,
                    JsonSerializer.Serialize(new { classes, likelihood = group.Max(o => o.Likelihood) }));
                Begin(key, record);
            }
        }

        internal void OnStateChanged(object sender, CameraStateChange change)
        {
            var camera = _directory.Find(change.CameraId);
            if (camera == null)
            {
                return;
            }

            var key = $"{camera.Id}|recorder|offline";
            var time = change.Timestamp.UtcDateTime;

            if (change.State == RecordingState.Recording)
            {
                End(key, time);
            }
            else if (change.State == RecordingState.Reconnecting)
            {
                Begin(key, NewEvent(camera, EventCategories.Offline, "Warning", "recorder", "Camera offline", null, time,
                    change.Error == null ? null : JsonSerializer.Serialize(new { error = change.Error })));
            }
        }

        internal void OnPluginEvent(object sender, PluginEvent pluginEvent)
        {
            var camera = _directory.Find(pluginEvent.CameraId);
            if (pluginEvent.CameraId != null && camera == null)
            {
                return;
            }

            // Plugins run for the recorder; an event about no camera belongs to every organization the recorder serves.
            var organizations = camera != null ? new[] { camera.OrganizationId } : _directory.All.Select(c => c.OrganizationId).Distinct().ToArray();
            var time = pluginEvent.Timestamp.UtcDateTime;

            foreach (var organization in organizations)
            {
                var record = new EventRecord
                {
                    Id = Guid.NewGuid().ToString("D"),
                    OrganizationId = organization,
                    SiteId = camera?.SiteId,
                    CameraId = camera?.Id,
                    Category = EventCategories.Plugin,
                    Severity = pluginEvent.Severity == PluginEventSeverity.Information ? "Info" : pluginEvent.Severity.ToString(),
                    Source = Truncate("plugin:" + pluginEvent.Source, 100),
                    Title = Truncate(string.IsNullOrWhiteSpace(pluginEvent.Message) ? pluginEvent.Type : pluginEvent.Message, 200),
                    Topic = Truncate(pluginEvent.Type, 256),
                    StartTime = time,
                    EndTime = time,
                    Data = pluginEvent.Data == null || pluginEvent.Data.Count == 0 ? null : JsonSerializer.Serialize(pluginEvent.Data)
                };

                Queue(new Operation(record, null));
                Publish(record);
            }
        }

        #endregion

        #region Private Methods

        private static EventRecord NewEvent(CameraEntry camera, string category, string severity, string source, string title, string topic, DateTime start, string data)
        {
            return new EventRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                OrganizationId = camera.OrganizationId,
                SiteId = camera.SiteId,
                CameraId = camera.Id,
                Category = category,
                Severity = severity,
                Source = source,
                Title = Truncate(title, 200),
                Topic = Truncate(topic, 256),
                StartTime = start,
                Data = data != null && data.Length > 4000 ? null : data
            };
        }

        /// <summary>Raised when an event starts (alerts listen); handlers must be quick.</summary>
        public event Action<EventRecord> Started;

        /// <summary>Raised when an event that was going on ends: its id, camera, start and end. Handlers must be quick.</summary>
        public event Action<string, string, DateTime, DateTime> Closed;

        private void PublishClosed(string id, string cameraId, DateTime start, DateTime end)
        {
            try
            {
                Closed?.Invoke(id, cameraId, start, end);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "An event listener failed");
            }
        }

        /// <summary>
        /// Events another gateway recorded for this one's cameras: stored now (the gateway forgets them once this returns),
        /// once each (an event already stored is skipped), and passed on like local ones (alerts, the retention index).
        /// </summary>
        public async Task ImportAsync(IReadOnlyCollection<EventRecord> adds, IReadOnlyCollection<(string Id, string CameraId, DateTime Start, DateTime End)> closes, CancellationToken cancellationToken = default)
        {
            var known = await _store.FindExistingAsync(adds.Select(a => a.Id).Concat(closes.Select(c => c.Id)).ToList(), cancellationToken).ConfigureAwait(false);
            var fresh = adds.Where(a => !known.ContainsKey(a.Id)).GroupBy(a => a.Id).Select(g => g.First()).ToList();
            if (fresh.Count > 0)
            {
                await _store.AddAsync(fresh, cancellationToken).ConfigureAwait(false);
            }

            // An end is only taken for an event of the same camera (stored before, or in this batch).
            var cameras = fresh.ToDictionary(a => a.Id, a => a.CameraId, StringComparer.Ordinal);
            foreach (var (id, camera) in known)
            {
                cameras.TryAdd(id, camera);
            }

            closes = closes.Where(c => cameras.TryGetValue(c.Id, out var camera) && string.Equals(camera, c.CameraId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (closes.Count > 0)
            {
                await _store.CloseAsync(closes.Select(c => (c.Id, c.End)).ToList(), cancellationToken).ConfigureAwait(false);
            }

            foreach (var add in fresh)
            {
                _index.Add(add.CameraId, add.StartTime, add.EndTime);
                Publish(add);
            }

            foreach (var close in closes)
            {
                _index.Close(close.CameraId, close.Start, close.End);
                PublishClosed(close.Id, close.CameraId, close.Start, close.End);
            }
        }

        /// <summary>The gateway that records a camera stopped reporting: the camera is offline, as far as anyone here can tell.</summary>
        public void BeginGatewayOffline(string cameraId, DateTime at)
        {
            var camera = _directory.Find(cameraId);
            if (camera != null)
            {
                Begin($"{camera.Id}|gateway|offline", NewEvent(camera, EventCategories.Offline, "Warning", "control plane", "Gateway offline", null, at, null));
            }
        }

        /// <summary>The gateway reports again.</summary>
        public void EndGatewayOffline(string cameraId, DateTime at) => End($"{cameraId}|gateway|offline", at);

        private void Publish(EventRecord record)
        {
            try
            {
                Started?.Invoke(record);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "An event listener failed");
            }
        }

        /// <summary>Starts an event unless one with the same key is going on.</summary>
        private void Begin(string key, EventRecord record)
        {
            lock (_sync)
            {
                if (_open.ContainsKey(key))
                {
                    return;
                }

                _open[key] = new OpenEvent(record.Id, record.CameraId, record.StartTime) { LastSeen = record.StartTime };
            }

            Queue(new Operation(record, null));
            Publish(record);
        }

        private void End(string key, DateTime end)
        {
            OpenEvent open;
            lock (_sync)
            {
                if (!_open.Remove(key, out open))
                {
                    return;
                }
            }

            var closed = end < open.Start ? open.Start : end;
            Queue(new Operation(null, (open.Id, open.CameraId, open.Start, closed)));
            PublishClosed(open.Id, open.CameraId, open.Start, closed);
        }

        /// <summary>Detections whose objects have not been seen for <see cref="DetectionGap"/> end at the last sighting.</summary>
        private void EndQuietDetections()
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            List<KeyValuePair<string, OpenEvent>> quiet;

            lock (_sync)
            {
                quiet = _open.Where(o => o.Key.Contains("|detection|", StringComparison.Ordinal) && now - o.Value.LastSeen > DetectionGap).ToList();
            }

            foreach (var (key, open) in quiet)
            {
                End(key, open.LastSeen);
            }
        }

        private void EndAll(DateTime end)
        {
            List<string> keys;
            lock (_sync)
            {
                keys = _open.Keys.ToList();
            }

            foreach (var key in keys)
            {
                End(key, end);
            }
        }

        private void Queue(Operation operation)
        {
            _queue.Writer.TryWrite(operation);
        }

        /// <summary>Writes what is queued, in batches; a failed batch is logged and skipped (events are not worth stopping recording for).</summary>
        private async Task FlushAsync(CancellationToken cancellationToken)
        {
            while (_queue.Reader.TryPeek(out _))
            {
                var adds = new List<EventRecord>();
                var closes = new List<(string Id, string CameraId, DateTime Start, DateTime End)>();

                while (adds.Count + closes.Count < BatchSize && _queue.Reader.TryRead(out var operation))
                {
                    if (operation.Add != null)
                    {
                        adds.Add(operation.Add);
                    }
                    else if (operation.Close is { } close)
                    {
                        // Closing an event added in this same batch: set its end before it is written.
                        var pending = adds.FirstOrDefault(a => a.Id == close.Id);
                        if (pending != null)
                        {
                            pending.EndTime = close.End;
                        }
                        else
                        {
                            closes.Add(close);
                        }
                    }
                }

                try
                {
                    if (adds.Count > 0)
                    {
                        await _store.AddAsync(adds, cancellationToken).ConfigureAwait(false);
                    }

                    if (closes.Count > 0)
                    {
                        await _store.CloseAsync(closes.Select(c => (c.Id, c.End)).ToList(), cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger?.LogError(ex, "Could not store {Count} event change(s)", adds.Count + closes.Count);
                }

                foreach (var add in adds)
                {
                    _index.Add(add.CameraId, add.StartTime, add.EndTime);
                }

                foreach (var close in closes)
                {
                    _index.Close(close.CameraId, close.Start, close.End);
                }
            }
        }

        private async Task LoadIndexAsync(CancellationToken cancellationToken)
        {
            try
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                foreach (var (cameraId, start, end) in await _store.GetSpansAsync(now - _directory.LongestEventRetention, cancellationToken).ConfigureAwait(false))
                {
                    // Left going on by a previous run: its end is unknown, so it counts until now.
                    _index.Add(cameraId, start, end ?? now);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Could not load the stored events: video around older events may be removed early");
            }
        }

        /// <summary>Removes events (and forgets their spans) older than their camera's policy keeps video around events.</summary>
        private async Task CleanupAsync(CancellationToken cancellationToken)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            try
            {
                foreach (var camera in _directory.All)
                {
                    await _store.DeleteBeforeAsync(camera.Id, now - camera.EventRetention, cancellationToken).ConfigureAwait(false);
                }

                await _store.DeleteOthersBeforeAsync(_directory.All.Select(c => c.Id).ToList(), now - CameralessRetention, cancellationToken).ConfigureAwait(false);
                _index.Prune(now - _directory.LongestEventRetention);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Could not remove old events");
            }
        }

        private static string Capitalize(string text) => string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static string Truncate(string text, int length) => text == null || text.Length <= length ? text : text.Substring(0, length);

        #endregion
    }
}
