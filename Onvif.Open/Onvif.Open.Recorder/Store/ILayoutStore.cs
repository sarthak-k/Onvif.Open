// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// Saved live view layouts in the settings database.
    /// </summary>
    public interface ILayoutStore
    {
        /// <summary>The user's own layouts and the organization's shared ones, by name.</summary>
        Task<IReadOnlyList<LayoutRecord>> GetVisibleAsync(string organizationId, string userId, CancellationToken cancellationToken = default);

        Task<LayoutRecord> FindAsync(string id, CancellationToken cancellationToken = default);

        Task<int> CountOwnedAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>Inserts or updates the layout (by id).</summary>
        Task SaveAsync(LayoutRecord layout, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="ILayoutStore"/> on EF Core.
    /// </summary>
    public sealed class EfLayoutStore : ILayoutStore
    {
        #region Constructor

        public EfLayoutStore(IDbContextFactory<RecorderDbContext> contexts)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        }

        #endregion

        #region Private Variables

        private readonly IDbContextFactory<RecorderDbContext> _contexts;

        #endregion

        #region Public Methods

        public async Task<IReadOnlyList<LayoutRecord>> GetVisibleAsync(string organizationId, string userId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var layouts = await db.Layouts.AsNoTracking()
                    .Where(l => l.OrganizationId == organizationId && (l.OwnerId == userId || l.OwnerId == null))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                return layouts.OrderBy(l => l.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.CreatedAt).ToList();
            }
        }

        public async Task<LayoutRecord> FindAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Layouts.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<int> CountOwnedAsync(string userId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Layouts.CountAsync(l => l.OwnerId == userId, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SaveAsync(LayoutRecord layout, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await db.Layouts.AnyAsync(l => l.Id == layout.Id, cancellationToken).ConfigureAwait(false))
                {
                    db.Layouts.Update(layout);
                }
                else
                {
                    db.Layouts.Add(layout);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Layouts.Where(l => l.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        #endregion
    }
}
