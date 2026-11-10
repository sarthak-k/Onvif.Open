// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Exports
{
    public static class ExportStatuses
    {
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Ready = "Ready";
        public const string Failed = "Failed";
        public const string Expired = "Expired";

        /// <summary>An export's life; Ready and Failed exports never run again (a new export is made instead).</summary>
        public static Onvif.Open.Core.StateMachines.StateMachine<string> Machine { get; } = new Onvif.Open.Core.StateMachines.StateMachine<string>(
            "Export",
            Queued,
            (Queued, Running, "its turn in the queue"),
            (Running, Running, "resumed after a restart"),
            (Running, Ready, "MP4 written and hashed"),
            (Running, Failed, "no video, or ffmpeg failed"),
            (Ready, Expired, "7 days passed: file deleted"));

        /// <summary>Exports that still have work to do (run again after a restart).</summary>
        public static bool IsUnfinished(string status) => Machine.CanMove(status, Running);
    }

    /// <summary>
    /// Exports and their share links in the settings database.
    /// </summary>
    public interface IExportStore
    {
        /// <summary>Newest first.</summary>
        Task<IReadOnlyList<ExportRecord>> GetAsync(string organizationId, int limit, CancellationToken cancellationToken = default);
        Task<ExportRecord> FindAsync(string id, CancellationToken cancellationToken = default);
        Task SaveAsync(ExportRecord export, CancellationToken cancellationToken = default);

        /// <summary>Exports waiting or interrupted (oldest first), to run after a restart.</summary>
        Task<IReadOnlyList<ExportRecord>> GetUnfinishedAsync(CancellationToken cancellationToken = default);

        /// <summary>Ready exports whose time is up.</summary>
        Task<IReadOnlyList<ExportRecord>> GetExpiredAsync(DateTime now, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ShareLinkRecord>> GetLinksAsync(string exportId, CancellationToken cancellationToken = default);
        Task<ShareLinkRecord> FindLinkAsync(string id, CancellationToken cancellationToken = default);
        Task<ShareLinkRecord> FindLinkByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
        Task SaveLinkAsync(ShareLinkRecord link, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="IExportStore"/> on EF Core.
    /// </summary>
    public sealed class EfExportStore : IExportStore
    {
        public EfExportStore(IDbContextFactory<RecorderDbContext> contexts)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        }

        private readonly IDbContextFactory<RecorderDbContext> _contexts;

        public Task<IReadOnlyList<ExportRecord>> GetAsync(string organizationId, int limit, CancellationToken cancellationToken = default) =>
            ListAsync(db => db.Exports.AsNoTracking().Where(e => e.OrganizationId == organizationId).OrderByDescending(e => e.CreatedAt).Take(limit), cancellationToken);

        public async Task<ExportRecord> FindAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Exports.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SaveAsync(ExportRecord export, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await db.Exports.AnyAsync(e => e.Id == export.Id, cancellationToken).ConfigureAwait(false))
                {
                    db.Exports.Update(export);
                }
                else
                {
                    db.Exports.Add(export);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public Task<IReadOnlyList<ExportRecord>> GetUnfinishedAsync(CancellationToken cancellationToken = default) =>
            ListAsync(db => db.Exports.AsNoTracking().Where(e => e.Status == ExportStatuses.Queued || e.Status == ExportStatuses.Running).OrderBy(e => e.CreatedAt), cancellationToken);

        public Task<IReadOnlyList<ExportRecord>> GetExpiredAsync(DateTime now, CancellationToken cancellationToken = default) =>
            ListAsync(db => db.Exports.AsNoTracking().Where(e => e.Status == ExportStatuses.Ready && e.ExpiresAt <= now), cancellationToken);

        public async Task<IReadOnlyList<ShareLinkRecord>> GetLinksAsync(string exportId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.ShareLinks.AsNoTracking().Where(l => l.ExportId == exportId).OrderByDescending(l => l.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<ShareLinkRecord> FindLinkAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.ShareLinks.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<ShareLinkRecord> FindLinkByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.ShareLinks.AsNoTracking().FirstOrDefaultAsync(l => l.TokenHash == tokenHash, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SaveLinkAsync(ShareLinkRecord link, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await db.ShareLinks.AnyAsync(l => l.Id == link.Id, cancellationToken).ConfigureAwait(false))
                {
                    db.ShareLinks.Update(link);
                }
                else
                {
                    db.ShareLinks.Add(link);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<IReadOnlyList<ExportRecord>> ListAsync(Func<RecorderDbContext, IQueryable<ExportRecord>> query, CancellationToken cancellationToken)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await query(db).ToListAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
