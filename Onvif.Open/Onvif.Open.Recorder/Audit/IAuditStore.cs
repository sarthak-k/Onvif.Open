// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Audit
{
    public sealed class AuditQuery
    {
        public string OrganizationId { get; set; }
        public string UserId { get; set; }

        /// <summary>An action ("camera.update") or an area ("camera").</summary>
        public string Action { get; set; }

        public DateTime? From { get; set; }
        public DateTime? To { get; set; }

        /// <summary>Paging: entries before this time (the oldest of the previous page).</summary>
        public DateTime? Before { get; set; }

        public int Limit { get; set; } = 100;
    }

    /// <summary>
    /// The audit log in the settings database.
    /// </summary>
    public interface IAuditStore
    {
        Task AddAsync(IReadOnlyCollection<AuditRecord> entries, CancellationToken cancellationToken = default);

        /// <summary>Newest first.</summary>
        Task<IReadOnlyList<AuditRecord>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default);

        Task<int> DeleteBeforeAsync(DateTime before, CancellationToken cancellationToken = default);
    }

    public sealed class EfAuditStore : IAuditStore
    {
        public EfAuditStore(IDbContextFactory<RecorderDbContext> contexts)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        }

        private readonly IDbContextFactory<RecorderDbContext> _contexts;

        public async Task AddAsync(IReadOnlyCollection<AuditRecord> entries, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                db.Audit.AddRange(entries);
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<AuditRecord>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var entries = db.Audit.AsNoTracking().Where(a => a.OrganizationId == query.OrganizationId);

                if (query.UserId != null)
                {
                    entries = entries.Where(a => a.UserId == query.UserId);
                }

                if (query.Action != null)
                {
                    var area = query.Action + ".";
                    entries = entries.Where(a => a.Action == query.Action || a.Action.StartsWith(area));
                }

                if (query.From.HasValue)
                {
                    var from = query.From.Value;
                    entries = entries.Where(a => a.CreatedAt >= from);
                }

                if (query.To.HasValue)
                {
                    var to = query.To.Value;
                    entries = entries.Where(a => a.CreatedAt < to);
                }

                if (query.Before.HasValue)
                {
                    var before = query.Before.Value;
                    entries = entries.Where(a => a.CreatedAt < before);
                }

                return await entries.OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(query.Limit, 1, 1000)).ToListAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<int> DeleteBeforeAsync(DateTime before, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Audit.Where(a => a.CreatedAt < before).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
