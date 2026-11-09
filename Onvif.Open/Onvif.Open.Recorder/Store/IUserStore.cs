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

        /// <summary>Deletes the user with their memberships and private layouts.</summary>
        Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

        Task<bool> AnyAsync(CancellationToken cancellationToken = default);

        /// <summary>The organizations the user belongs to, oldest membership first.</summary>
        Task<IReadOnlyList<MembershipRecord>> GetMembershipsAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>The members of an organization.</summary>
        Task<IReadOnlyList<MembershipRecord>> GetMembersAsync(string organizationId, CancellationToken cancellationToken = default);

        Task<MembershipRecord> FindMembershipAsync(string userId, string organizationId, CancellationToken cancellationToken = default);

        /// <summary>Inserts or updates the membership (by id).</summary>
        Task SaveMembershipAsync(MembershipRecord membership, CancellationToken cancellationToken = default);
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
                // The user's memberships and private layouts go with the user.
                await db.Layouts.Where(l => l.OwnerId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                await db.Memberships.Where(m => m.UserId == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                return await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        public async Task<bool> AnyAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<MembershipRecord>> GetMembershipsAsync(string userId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var memberships = await db.Memberships.AsNoTracking().Where(m => m.UserId == userId).ToListAsync(cancellationToken).ConfigureAwait(false);
                return memberships.OrderBy(m => m.CreatedAt).ToList();
            }
        }

        public async Task<IReadOnlyList<MembershipRecord>> GetMembersAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Memberships.AsNoTracking().Where(m => m.OrganizationId == organizationId).ToListAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<MembershipRecord> FindMembershipAsync(string userId, string organizationId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Memberships.AsNoTracking().FirstOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SaveMembershipAsync(MembershipRecord membership, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await db.Memberships.AnyAsync(m => m.Id == membership.Id, cancellationToken).ConfigureAwait(false))
                {
                    db.Memberships.Update(membership);
                }
                else
                {
                    db.Memberships.Add(membership);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
