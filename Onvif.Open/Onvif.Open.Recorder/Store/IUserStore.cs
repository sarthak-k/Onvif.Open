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
    /// Users in the settings database.
    /// </summary>
    public interface IUserStore
    {
        Task<IReadOnlyList<UserRecord>> GetUsersAsync(CancellationToken cancellationToken = default);
        Task<UserRecord> FindByIdAsync(string id, CancellationToken cancellationToken = default);
        Task<UserRecord> FindByNameAsync(string userName, CancellationToken cancellationToken = default);

        /// <summary>Inserts or updates the user (by id).</summary>
        Task SaveAsync(UserRecord user, CancellationToken cancellationToken = default);

        Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="IUserStore"/> on EF Core.
    /// </summary>
    public sealed class EfUserStore : IUserStore
    {
        #region Constructor

        public EfUserStore(IDbContextFactory<RecorderDbContext> contexts)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        }

        #endregion

        #region Private Variables

        private readonly IDbContextFactory<RecorderDbContext> _contexts;

        #endregion

        #region Public Methods

        public static string Normalize(string userName)
        {
            return (userName ?? string.Empty).Trim().ToUpperInvariant();
        }

        public async Task<IReadOnlyList<UserRecord>> GetUsersAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var users = await db.Users.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                return users.OrderBy(u => u.NormalizedUserName, StringComparer.Ordinal).ToList();
            }
        }

        public async Task<UserRecord> FindByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<UserRecord> FindByNameAsync(string userName, CancellationToken cancellationToken = default)
        {
            var normalized = Normalize(userName);

            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedUserName == normalized, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                user.NormalizedUserName = Normalize(user.UserName);
                user.UpdatedAt = DateTime.UtcNow;

                if (await db.Users.AnyAsync(u => u.Id == user.Id, cancellationToken).ConfigureAwait(false))
                {
                    db.Users.Update(user);
                }
                else
                {
                    db.Users.Add(user);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        #endregion
    }
}
