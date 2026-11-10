// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Events
{
    /// <summary>What to look for in the stored events.</summary>
    public sealed class EventQuery
    {
        public string OrganizationId { get; set; }

        /// <summary>Only these sites; null: all sites (events about no camera are included only then).</summary>
        public IReadOnlySet<string> Sites { get; set; }

        public string CameraId { get; set; }
        public string SiteId { get; set; }
        public string Category { get; set; }

        /// <summary>Events going on at or after this time (end time at or after it, or no end).</summary>
        public DateTime? From { get; set; }

        /// <summary>Events that started before this time.</summary>
        public DateTime? To { get; set; }

        /// <summary>Paging: events that started before this time (the oldest start time of the previous page).</summary>
        public DateTime? Before { get; set; }

        public int Limit { get; set; } = 100;
    }

    /// <summary>
    /// Events in the settings database.
    /// </summary>
    public interface IEventStore
    {
        Task AddAsync(IReadOnlyCollection<EventRecord> events, CancellationToken cancellationToken = default);

        /// <summary>Sets the end time of events that were going on.</summary>
        Task CloseAsync(IReadOnlyCollection<(string Id, DateTime End)> ends, CancellationToken cancellationToken = default);

        /// <summary>Newest first.</summary>
        Task<IReadOnlyList<EventRecord>> QueryAsync(EventQuery query, CancellationToken cancellationToken = default);

        /// <summary>The time spans of events of cameras since a time (for retention), oldest first.</summary>
        Task<IReadOnlyList<(string CameraId, DateTime Start, DateTime? End)>> GetSpansAsync(DateTime since, CancellationToken cancellationToken = default);

        /// <summary>Removes a camera's events that started before <paramref name="before"/>; null camera: events about no camera.</summary>
        Task<int> DeleteBeforeAsync(string cameraId, DateTime before, CancellationToken cancellationToken = default);

        /// <summary>Removes events that started before <paramref name="before"/> about no camera or a camera not in <paramref name="cameraIds"/> (removed ones).</summary>
        Task<int> DeleteOthersBeforeAsync(IReadOnlyCollection<string> cameraIds, DateTime before, CancellationToken cancellationToken = default);

        /// <summary>Which of these event ids are stored already, with the camera each is about.</summary>
        Task<IReadOnlyDictionary<string, string>> FindExistingAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="IEventStore"/> on EF Core.
    /// </summary>
    public sealed class EfEventStore : IEventStore
    {
        public EfEventStore(IDbContextFactory<RecorderDbContext> contexts)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        }

        private readonly IDbContextFactory<RecorderDbContext> _contexts;

        public async Task<IReadOnlyDictionary<string, string>> FindExistingAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken = default)
        {
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            if (ids.Count == 0)
            {
                return found;
            }

            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var chunk in ids.Distinct().Chunk(500))
                {
                    foreach (var e in await db.Events.AsNoTracking().Where(e => chunk.Contains(e.Id)).Select(e => new { e.Id, e.CameraId }).ToListAsync(cancellationToken).ConfigureAwait(false))
                    {
                        found[e.Id] = e.CameraId;
                    }
                }

                return found;
            }
        }

        public async Task AddAsync(IReadOnlyCollection<EventRecord> events, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                db.Events.AddRange(events);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task CloseAsync(IReadOnlyCollection<(string Id, DateTime End)> ends, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var (id, end) in ends)
                {
                    await db.Events.Where(e => e.Id == id).ExecuteUpdateAsync(set => set.SetProperty(e => e.EndTime, end), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        public async Task<IReadOnlyList<EventRecord>> QueryAsync(EventQuery query, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var events = db.Events.AsNoTracking().Where(e => e.OrganizationId == query.OrganizationId);

                if (query.Sites != null)
                {
                    var sites = query.Sites.ToList();
                    events = events.Where(e => e.SiteId != null && sites.Contains(e.SiteId));
                }

                if (query.CameraId != null)
                {
                    events = events.Where(e => e.CameraId == query.CameraId);
                }

                if (query.SiteId != null)
                {
                    events = events.Where(e => e.SiteId == query.SiteId);
                }

                if (query.Category != null)
                {
                    events = events.Where(e => e.Category == query.Category);
                }

                if (query.From.HasValue)
                {
                    var from = query.From.Value;
                    events = events.Where(e => e.StartTime >= from || e.EndTime == null || e.EndTime >= from);
                }

                if (query.To.HasValue)
                {
                    var to = query.To.Value;
                    events = events.Where(e => e.StartTime < to);
                }

                if (query.Before.HasValue)
                {
                    var before = query.Before.Value;
                    events = events.Where(e => e.StartTime < before);
                }

                return await events.OrderByDescending(e => e.StartTime).Take(Math.Clamp(query.Limit, 1, 1000)).ToListAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<(string CameraId, DateTime Start, DateTime? End)>> GetSpansAsync(DateTime since, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var spans = await db.Events.AsNoTracking()
                    .Where(e => e.CameraId != null && (e.StartTime >= since || e.EndTime == null || e.EndTime >= since))
                    .OrderBy(e => e.StartTime)
                    .Select(e => new { e.CameraId, e.StartTime, e.EndTime })
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                return spans.Select(s => (s.CameraId, s.StartTime, s.EndTime)).ToList();
            }
        }

        public async Task<int> DeleteBeforeAsync(string cameraId, DateTime before, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Events.Where(e => e.CameraId == cameraId && e.StartTime < before && e.EndTime != null && e.EndTime < before)
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<int> DeleteOthersBeforeAsync(IReadOnlyCollection<string> cameraIds, DateTime before, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Events.Where(e => (e.CameraId == null || !cameraIds.Contains(e.CameraId)) && e.StartTime < before && e.EndTime != null && e.EndTime < before)
                    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
