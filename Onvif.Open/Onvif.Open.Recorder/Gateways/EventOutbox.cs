// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>An event change on its way to the control plane: an event that started, or one that ended.</summary>
    public sealed record UpstreamEvent(string Kind, EventRecord Event, string Id, string CameraId, DateTime? Start, DateTime? End)
    {
        public const string Started = "start";
        public const string Ended = "end";
    }

    /// <summary>
    /// The gateway's outbox of events for the control plane: what it records about the control plane's cameras, kept on
    /// disk (one line per change) until the control plane has stored it, so nothing is lost while the site is cut off
    /// or the gateway restarts; it catches up when the control plane is back. At most <see cref="MaxPending"/> changes
    /// wait (the oldest go first, with a warning).
    /// </summary>
    public sealed class EventOutbox
    {
        public EventOutbox(EventRecorder recorder, AssignedCameras assigned, string path, ILogger<EventOutbox> logger)
        {
            _assigned = assigned ?? throw new ArgumentNullException(nameof(assigned));
            _path = path;
            _logger = logger;
            Load();
            recorder.Started += OnStarted;
            recorder.Closed += OnClosed;
        }

        public const int MaxPending = 100_000;
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly AssignedCameras _assigned;
        private readonly string _path;
        private readonly ILogger _logger;
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();
        private readonly List<UpstreamEvent> _pending = new List<UpstreamEvent>();

        /// <summary>Changes waiting for the control plane.</summary>
        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _pending.Count;
                }
            }
        }

        /// <summary>The oldest changes waiting, at most <paramref name="max"/>.</summary>
        public IReadOnlyList<UpstreamEvent> Peek(int max)
        {
            lock (_sync)
            {
                return _pending.Take(max).ToList();
            }
        }

        /// <summary>The control plane stored the oldest <paramref name="count"/> changes: they are forgotten here.</summary>
        public void Remove(int count)
        {
            lock (_sync)
            {
                _pending.RemoveRange(0, Math.Min(count, _pending.Count));
                Rewrite();
            }
        }

        private void OnStarted(EventRecord record)
        {
            if (IsAssigned(record.CameraId))
            {
                Append(new UpstreamEvent(UpstreamEvent.Started, record, record.Id, record.CameraId, record.StartTime, record.EndTime));
            }
        }

        private void OnClosed(string id, string cameraId, DateTime start, DateTime end)
        {
            if (IsAssigned(cameraId))
            {
                Append(new UpstreamEvent(UpstreamEvent.Ended, null, id, cameraId, start, end));
            }
        }

        private bool IsAssigned(string cameraId) => cameraId != null && _assigned.CameraIds.Contains(cameraId, StringComparer.OrdinalIgnoreCase);

        private void Append(UpstreamEvent change)
        {
            lock (_sync)
            {
                _pending.Add(change);
                if (_pending.Count > MaxPending)
                {
                    var dropped = _pending.Count - MaxPending;
                    _pending.RemoveRange(0, dropped);
                    _logger?.LogWarning("The control plane has been out of reach for long: {Count} old event change(s) dropped", dropped);
                    Rewrite();
                    return;
                }

                if (_path != null)
                {
                    try
                    {
                        File.AppendAllText(_path, JsonSerializer.Serialize(change, Json) + "\n");
                    }
                    catch (IOException ex)
                    {
                        _logger?.LogWarning("Could not keep an event for the control plane on disk: {Error}", ex.Message);
                    }
                }
            }
        }

        private void Load()
        {
            if (_path == null)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
            if (!File.Exists(_path))
            {
                return;
            }

            foreach (var line in File.ReadAllLines(_path).Where(l => l.Length > 0))
            {
                try
                {
                    _pending.Add(JsonSerializer.Deserialize<UpstreamEvent>(line, Json));
                }
                catch (JsonException)
                {
                    // A line cut short by a crash: the rest is still good.
                }
            }
        }

        private void Rewrite()
        {
            if (_path == null)
            {
                return;
            }

            var temporary = _path + ".tmp";
            File.WriteAllLines(temporary, _pending.Select(p => JsonSerializer.Serialize(p, Json)));
            File.Move(temporary, _path, true);
        }
    }
}
