// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Tenancy
{
    /// <summary>
    /// Organizations, sites and recording policies in the settings database.
    /// </summary>
    public interface ITenancyStore
    {
        Task<OrganizationRecord> FindOrganizationAsync(string id, CancellationToken cancellationToken = default);
        Task SaveOrganizationAsync(OrganizationRecord organization, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SiteRecord>> GetSitesAsync(string organizationId, CancellationToken cancellationToken = default);
        Task<SiteRecord> FindSiteAsync(string id, CancellationToken cancellationToken = default);
        Task SaveSiteAsync(SiteRecord site, CancellationToken cancellationToken = default);
        Task<bool> DeleteSiteAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>All policies of an organization; null: of every organization (the recorder applies them all).</summary>
        Task<IReadOnlyList<RecordingPolicyRecord>> GetPoliciesAsync(string organizationId, CancellationToken cancellationToken = default);
        Task<RecordingPolicyRecord> FindPolicyAsync(string id, CancellationToken cancellationToken = default);
        Task SavePolicyAsync(RecordingPolicyRecord policy, CancellationToken cancellationToken = default);
        Task<bool> DeletePolicyAsync(string id, CancellationToken cancellationToken = default);

        Task<int> CountCamerasAsync(string siteId = null, string policyId = null, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="ITenancyStore"/> on EF Core.
    /// </summary>
    public sealed class EfTenancyStore : ITenancyStore
    {
        public EfTenancyStore(IDbContextFactory<RecorderDbContext> contexts)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        }

        private readonly IDbContextFactory<RecorderDbContext> _contexts;

        public Task<OrganizationRecord> FindOrganizationAsync(string id, CancellationToken cancellationToken = default) =>
            QueryAsync(db => db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken), cancellationToken);

        public Task SaveOrganizationAsync(OrganizationRecord organization, CancellationToken cancellationToken = default) =>
            UpsertAsync(db => db.Organizations, organization, o => o.Id == organization.Id, cancellationToken);

        public async Task<IReadOnlyList<SiteRecord>> GetSitesAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            var sites = await QueryAsync(db => db.Sites.AsNoTracking().Where(s => s.OrganizationId == organizationId).ToListAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            return sites.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public Task<SiteRecord> FindSiteAsync(string id, CancellationToken cancellationToken = default) =>
            QueryAsync(db => db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, cancellationToken), cancellationToken);

        public Task SaveSiteAsync(SiteRecord site, CancellationToken cancellationToken = default) =>
            UpsertAsync(db => db.Sites, site, s => s.Id == site.Id, cancellationToken);

        public async Task<bool> DeleteSiteAsync(string id, CancellationToken cancellationToken = default) =>
            await QueryAsync(db => db.Sites.Where(s => s.Id == id).ExecuteDeleteAsync(cancellationToken), cancellationToken).ConfigureAwait(false) > 0;

        public async Task<IReadOnlyList<RecordingPolicyRecord>> GetPoliciesAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            var policies = await QueryAsync(db => db.RecordingPolicies.AsNoTracking().Where(p => organizationId == null || p.OrganizationId == organizationId).ToListAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
            return policies.OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public Task<RecordingPolicyRecord> FindPolicyAsync(string id, CancellationToken cancellationToken = default) =>
            QueryAsync(db => db.RecordingPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken), cancellationToken);

        public Task SavePolicyAsync(RecordingPolicyRecord policy, CancellationToken cancellationToken = default) =>
            UpsertAsync(db => db.RecordingPolicies, policy, p => p.Id == policy.Id, cancellationToken);

        public async Task<bool> DeletePolicyAsync(string id, CancellationToken cancellationToken = default) =>
            await QueryAsync(db => db.RecordingPolicies.Where(p => p.Id == id).ExecuteDeleteAsync(cancellationToken), cancellationToken).ConfigureAwait(false) > 0;

        public Task<int> CountCamerasAsync(string siteId = null, string policyId = null, CancellationToken cancellationToken = default) =>
            QueryAsync(db => db.Cameras.CountAsync(c => (siteId == null || c.SiteId == siteId) && (policyId == null || c.PolicyId == policyId), cancellationToken), cancellationToken);

        private async Task<T> QueryAsync<T>(Func<RecorderDbContext, Task<T>> query, CancellationToken cancellationToken)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await query(db).ConfigureAwait(false);
            }
        }

        private async Task UpsertAsync<T>(Func<RecorderDbContext, DbSet<T>> set, T record, System.Linq.Expressions.Expression<Func<T, bool>> match, CancellationToken cancellationToken)
            where T : class
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await set(db).AnyAsync(match, cancellationToken).ConfigureAwait(false))
                {
                    set(db).Update(record);
                }
                else
                {
                    set(db).Add(record);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
