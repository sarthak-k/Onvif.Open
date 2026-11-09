// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Cases
{
    /// <summary>
    /// Cases and their clips in the settings database.
    /// </summary>
    public interface ICaseStore
    {
        /// <summary>Most recently changed first; with the number of clips of each.</summary>
        Task<IReadOnlyList<(CaseRecord Case, int Clips)>> GetAsync(string organizationId, string status, CancellationToken cancellationToken = default);
        Task<CaseRecord> FindAsync(string id, CancellationToken cancellationToken = default);
        Task SaveAsync(CaseRecord record, CancellationToken cancellationToken = default);
        Task DeleteAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>Oldest first.</summary>
        Task<IReadOnlyList<CaseClipRecord>> GetClipsAsync(string caseId, CancellationToken cancellationToken = default);
        Task<CaseClipRecord> FindClipAsync(string clipId, CancellationToken cancellationToken = default);
        Task SaveClipAsync(CaseClipRecord clip, CancellationToken cancellationToken = default);
        Task<bool> DeleteClipAsync(string clipId, CancellationToken cancellationToken = default);

        /// <summary>The clips of all open cases (their video is kept).</summary>
        Task<IReadOnlyList<CaseClipRecord>> GetHeldClipsAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="ICaseStore"/> on EF Core.
    /// </summary>
    public sealed class EfCaseStore : ICaseStore
    {
        public EfCaseStore(IDbContextFactory<RecorderDbContext> contexts)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        }

        private readonly IDbContextFactory<RecorderDbContext> _contexts;

        public async Task<IReadOnlyList<(CaseRecord Case, int Clips)>> GetAsync(string organizationId, string status, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var cases = db.Cases.AsNoTracking().Where(c => c.OrganizationId == organizationId);
                if (status != null)
                {
                    cases = cases.Where(c => c.Status == status);
                }

                var found = await cases.OrderByDescending(c => c.UpdatedAt).Take(500).ToListAsync(cancellationToken).ConfigureAwait(false);
                var ids = found.Select(c => c.Id).ToList();
                var counts = await db.CaseClips.AsNoTracking().Where(c => ids.Contains(c.CaseId))
                    .GroupBy(c => c.CaseId).Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken).ConfigureAwait(false);
                return found.Select(c => (c, counts.TryGetValue(c.Id, out var n) ? n : 0)).ToList();
            }
        }

        public async Task<CaseRecord> FindAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Cases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SaveAsync(CaseRecord record, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await db.Cases.AnyAsync(c => c.Id == record.Id, cancellationToken).ConfigureAwait(false))
                {
                    db.Cases.Update(record);
                }
                else
                {
                    db.Cases.Add(record);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                await db.CaseClips.Where(c => c.CaseId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                await db.Cases.Where(c => c.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<CaseClipRecord>> GetClipsAsync(string caseId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.CaseClips.AsNoTracking().Where(c => c.CaseId == caseId).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<CaseClipRecord> FindClipAsync(string clipId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.CaseClips.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clipId, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SaveClipAsync(CaseClipRecord clip, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await db.CaseClips.AnyAsync(c => c.Id == clip.Id, cancellationToken).ConfigureAwait(false))
                {
                    db.CaseClips.Update(clip);
                }
                else
                {
                    db.CaseClips.Add(clip);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<bool> DeleteClipAsync(string clipId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.CaseClips.Where(c => c.Id == clipId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        public async Task<IReadOnlyList<CaseClipRecord>> GetHeldClipsAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var open = db.Cases.Where(c => c.Status == CaseStatuses.Open).Select(c => c.Id);
                return await db.CaseClips.AsNoTracking().Where(c => open.Contains(c.CaseId)).ToListAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
