// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Billing
{
    /// <summary>A plan to make, or what to change in one.</summary>
    public sealed class PlanRequest
    {
        /// <summary>New plans only: lower case letters, digits and dashes (e.g. "starter").</summary>
        public string Code { get; set; }

        public string Name { get; set; }
        public int Cameras { get; set; }
        public double StorageGB { get; set; }
        public decimal MonthlyPrice { get; set; }
        public string ProviderPriceId { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>A plan with how many organizations are on it.</summary>
    public sealed record PlanUsage(PlanRecord Plan, int Organizations);

    /// <summary>What a change did: the plan now, and the one it replaced when the change made a new version.</summary>
    public sealed record PlanChange(PlanRecord Plan, PlanRecord Previous);

    /// <summary>
    /// The plans, in the database: the record of what was sold, at what price, to whom. A plan's name and order change
    /// in place; its price or limits change in place only while no organization is on it, else into a new version that
    /// new subscriptions get, while the organizations on the old one keep it. Retired plans are not offered any more and
    /// are never deleted.
    /// </summary>
    public sealed partial class PlanService
    {
        public PlanService(IDbContextFactory<RecorderDbContext> contexts, BillingOptions options, ILogger<PlanService> logger, TimeProvider timeProvider = null)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _options = options ?? new BillingOptions();
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        private readonly IDbContextFactory<RecorderDbContext> _contexts;
        private readonly BillingOptions _options;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);

        [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
        private static partial Regex ValidCode();

        private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

        /// <summary>Puts the configured plans whose code is not in the database yet; then the trial plan must be there.</summary>
        public async Task SeedAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var codes = await db.Plans.Select(p => p.Code).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
                var order = 0;
                foreach (var seed in _options.Plans)
                {
                    order++;
                    var code = seed.Id?.Trim().ToLowerInvariant();
                    if (code == null || codes.Contains(code))
                    {
                        continue;
                    }

                    db.Plans.Add(new PlanRecord
                    {
                        Id = Guid.NewGuid().ToString("D"),
                        Code = code,
                        Version = 1,
                        Name = seed.Name.Trim(),
                        Cameras = seed.Cameras,
                        StorageGB = seed.StorageGB,
                        MonthlyPrice = seed.MonthlyPrice,
                        ProviderPriceId = string.IsNullOrWhiteSpace(seed.ProviderPriceId) ? null : seed.ProviderPriceId.Trim(),
                        Active = true,
                        SortOrder = order * 10,
                        CreatedAt = Now,
                        UpdatedAt = Now
                    });
                    _logger?.LogInformation("Plan {Code} added from the configuration", code);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            if (await FindActiveAsync(_options.TrialPlan, cancellationToken).ConfigureAwait(false) == null)
            {
                throw new InvalidOperationException($"Billing:TrialPlan '{_options.TrialPlan}' is not an active plan: add it to Billing:Plans, or make it in Settings › Plans.");
            }
        }

        /// <summary>Every plan, with how many organizations are on each (operators).</summary>
        public async Task<IReadOnlyList<PlanUsage>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var plans = await db.Plans.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
                var counts = await db.Organizations.AsNoTracking().Where(o => o.PlanId != null).GroupBy(o => o.PlanId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken).ConfigureAwait(false);
                return Ordered(plans).Select(p => new PlanUsage(p, counts.FirstOrDefault(c => c.Key == p.Id)?.Count ?? 0)).ToList();
            }
        }

        /// <summary>The plans offered to new subscriptions.</summary>
        public async Task<IReadOnlyList<PlanRecord>> GetActiveAsync(CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return Ordered(await db.Plans.AsNoTracking().Where(p => p.Active).ToListAsync(cancellationToken).ConfigureAwait(false)).ToList();
            }
        }

        public async Task<PlanRecord> FindAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return id == null ? null : await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>The offered version of a plan.</summary>
        public async Task<PlanRecord> FindActiveAsync(string code, CancellationToken cancellationToken = default)
        {
            var normalized = code?.Trim().ToLowerInvariant();
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return normalized == null ? null : await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == normalized && p.Active, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>The plan (any version, retired too) sold at this provider price.</summary>
        public async Task<PlanRecord> FindByPriceAsync(string priceId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return priceId == null ? null : await db.Plans.AsNoTracking().OrderByDescending(p => p.Version).FirstOrDefaultAsync(p => p.ProviderPriceId == priceId, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<ServiceResult<PlanRecord>> CreateAsync(PlanRequest request, CancellationToken cancellationToken = default)
        {
            var code = request?.Code?.Trim().ToLowerInvariant();
            var errors = Check(request);
            if (code == null || !ValidCode().IsMatch(code))
            {
                errors.Add("Code is lower case letters, digits and dashes, e.g. starter.");
            }

            if (errors.Count > 0)
            {
                return ServiceResult<PlanRecord>.Invalid(errors);
            }

            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await db.Plans.AnyAsync(p => p.Code == code, cancellationToken).ConfigureAwait(false))
                    {
                        return ServiceResult<PlanRecord>.Conflict($"There is a plan {code} already: change it instead.");
                    }

                    var plan = New(code, 1, request);
                    db.Plans.Add(plan);
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return ServiceResult<PlanRecord>.Ok(plan);
                }
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Changes an offered plan: in place, or (price or limits, with organizations on it) into a new version.</summary>
        public async Task<ServiceResult<PlanChange>> UpdateAsync(string id, PlanRequest request, CancellationToken cancellationToken = default)
        {
            var errors = Check(request);
            if (errors.Count > 0)
            {
                return ServiceResult<PlanChange>.Invalid(errors);
            }

            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
                    if (plan == null)
                    {
                        return ServiceResult<PlanChange>.NotFound();
                    }

                    if (!plan.Active)
                    {
                        return ServiceResult<PlanChange>.Conflict("This plan is retired: change the version that replaced it.");
                    }

                    var previous = Copy(plan);
                    var terms = plan.Cameras != request.Cameras || plan.StorageGB != request.StorageGB || plan.MonthlyPrice != request.MonthlyPrice || plan.ProviderPriceId != Clean(request.ProviderPriceId);
                    var used = await db.Organizations.AnyAsync(o => o.PlanId == plan.Id, cancellationToken).ConfigureAwait(false);
                    if (terms && used)
                    {
                        // What was sold stays as sold: the organizations on it keep it, new subscriptions get the new version.
                        var next = New(plan.Code, await db.Plans.Where(p => p.Code == plan.Code).MaxAsync(p => p.Version, cancellationToken).ConfigureAwait(false) + 1, request);
                        plan.Active = false;
                        plan.RetiredAt = Now;
                        plan.UpdatedAt = Now;
                        plan.ReplacedById = next.Id;
                        db.Plans.Add(next);
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        _logger?.LogInformation("Plan {Code} version {Version} replaced by version {Next}", plan.Code, plan.Version, next.Version);
                        return ServiceResult<PlanChange>.Ok(new PlanChange(next, previous));
                    }

                    Apply(plan, request);
                    plan.UpdatedAt = Now;
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return ServiceResult<PlanChange>.Ok(new PlanChange(plan, previous));
                }
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Stops offering a plan; the organizations on it keep it. Not the trial plan.</summary>
        public async Task<ServiceResult<PlanRecord>> RetireAsync(string id, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken).ConfigureAwait(false);
                    if (plan == null)
                    {
                        return ServiceResult<PlanRecord>.NotFound();
                    }

                    if (string.Equals(plan.Code, _options.TrialPlan, StringComparison.OrdinalIgnoreCase))
                    {
                        return ServiceResult<PlanRecord>.Conflict("New organizations try this plan (Billing:TrialPlan): it stays offered.");
                    }

                    if (plan.Active)
                    {
                        plan.Active = false;
                        plan.RetiredAt = Now;
                        plan.UpdatedAt = Now;
                        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    return ServiceResult<PlanRecord>.Ok(plan);
                }
            }
            finally
            {
                _changes.Release();
            }
        }

        private static IEnumerable<PlanRecord> Ordered(IEnumerable<PlanRecord> plans) =>
            plans.OrderBy(p => p.SortOrder).ThenBy(p => p.Code, StringComparer.Ordinal).ThenByDescending(p => p.Version);

        private List<string> Check(PlanRequest request)
        {
            var errors = new List<string>();
            if (request == null || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100)
            {
                errors.Add("Name is required and at most 100 characters.");
            }

            if (request != null && (request.Cameras < 0 || request.StorageGB < 0 || request.MonthlyPrice < 0))
            {
                errors.Add("Cameras, storage and price are 0 or more (0 cameras or storage: no limit).");
            }

            if (request != null && request.MonthlyPrice > 0 && string.IsNullOrWhiteSpace(request.ProviderPriceId) && _options.Stripe.IsConfigured)
            {
                errors.Add("A paid plan needs the payment provider's price (Stripe: price_...).");
            }

            return errors;
        }

        private PlanRecord New(string code, int version, PlanRequest request)
        {
            var plan = new PlanRecord { Id = Guid.NewGuid().ToString("D"), Code = code, Version = version, Active = true, CreatedAt = Now, UpdatedAt = Now };
            Apply(plan, request);
            return plan;
        }

        private static void Apply(PlanRecord plan, PlanRequest request)
        {
            plan.Name = request.Name.Trim();
            plan.Cameras = request.Cameras;
            plan.StorageGB = request.StorageGB;
            plan.MonthlyPrice = request.MonthlyPrice;
            plan.ProviderPriceId = Clean(request.ProviderPriceId);
            plan.SortOrder = request.SortOrder;
        }

        private static string Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        /// <summary>The plan as it was (for the audit's before and after).</summary>
        private static PlanRecord Copy(PlanRecord plan) => new PlanRecord
        {
            Id = plan.Id,
            Code = plan.Code,
            Version = plan.Version,
            Name = plan.Name,
            Cameras = plan.Cameras,
            StorageGB = plan.StorageGB,
            MonthlyPrice = plan.MonthlyPrice,
            ProviderPriceId = plan.ProviderPriceId,
            Active = plan.Active,
            SortOrder = plan.SortOrder,
            ReplacedById = plan.ReplacedById,
            CreatedAt = plan.CreatedAt,
            UpdatedAt = plan.UpdatedAt,
            RetiredAt = plan.RetiredAt
        };
    }
}
