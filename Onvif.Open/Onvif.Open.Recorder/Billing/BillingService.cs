// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Billing
{
    /// <summary>
    /// Where an organization's subscription stands. Stored by name (<see cref="OrganizationRecord.SubscriptionState"/>)
    /// and sent by name: adding one is compatible, renaming one is not.
    /// </summary>
    public enum SubscriptionState
    {
        Trialing,
        Active,
        PastDue,
        Canceled
    }

    /// <summary>The subscription's lifecycle (docs/STATE-MACHINES.md).</summary>
    public static class SubscriptionStates
    {
        public static Onvif.Open.Core.StateMachines.StateMachine<SubscriptionState> Machine { get; } = new Onvif.Open.Core.StateMachines.StateMachine<SubscriptionState>(
            "Subscription",
            SubscriptionState.Trialing,
            (SubscriptionState.Trialing, SubscriptionState.Active, "paid for a plan"),
            (SubscriptionState.Trialing, SubscriptionState.Canceled, "the trial ended unpaid"),
            (SubscriptionState.Active, SubscriptionState.PastDue, "a payment failed"),
            (SubscriptionState.PastDue, SubscriptionState.Active, "paid"),
            (SubscriptionState.PastDue, SubscriptionState.Canceled, "unpaid for too long, or canceled"),
            (SubscriptionState.Active, SubscriptionState.Canceled, "canceled"),
            (SubscriptionState.Canceled, SubscriptionState.Active, "subscribed again"));
    }

    /// <summary>What an organization uses of its plan.</summary>
    public sealed record BillingUsage(int Cameras, long StorageBytes);

    /// <summary>An organization's plan, subscription and usage.</summary>
    public sealed record BillingStatus(OrganizationRecord Organization, PlanRecord Plan, BillingUsage Usage)
    {
        public bool OverCameras => Plan != null && Plan.Cameras > 0 && Usage.Cameras > Plan.Cameras;
        public bool OverStorage => Plan != null && Plan.StorageGB > 0 && Usage.StorageBytes > Plan.StorageGB * 1024 * 1024 * 1024;
    }

    /// <summary>
    /// Plans and subscriptions: new organizations try <see cref="BillingOptions.TrialPlan"/>; owners pay for a plan at
    /// the payment provider, whose webhooks keep the subscription's state here. A plan limits how many cameras can be
    /// added; recording itself never stops for billing (video is evidence): going over is shown, not enforced.
    /// </summary>
    public sealed class BillingService : BackgroundService
    {
        #region Constructor

        public BillingService(BillingOptions options, IBillingProvider provider, PlanService plans, IDbContextFactory<RecorderDbContext> contexts, ISettingsStore cameras, Gateways.GatewayService gateways, ILogger<BillingService> logger, TimeProvider timeProvider = null)
        {
            Options = options ?? new BillingOptions();
            Plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
            _gateways = gateways ?? throw new ArgumentNullException(nameof(gateways));
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Variables

        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        public BillingOptions Options { get; }

        public PlanService Plans { get; }

        /// <summary>Whether this organization runs the service (its owners manage the plans).</summary>
        public bool IsOperator(string organizationId) => Options.Enabled && organizationId != null && string.Equals(organizationId, Options.OperatorOrganizationId, StringComparison.OrdinalIgnoreCase);

        public bool CanPay => Options.Enabled && _provider.IsConfigured;

        private readonly IBillingProvider _provider;
        private readonly IDbContextFactory<RecorderDbContext> _contexts;
        private readonly ISettingsStore _cameras;
        private readonly Gateways.GatewayService _gateways;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);

        private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

        #endregion

        #region Public Methods

        /// <summary>The organization's plan and use of it; null where billing is off.</summary>
        public async Task<BillingStatus> GetAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            if (!Options.Enabled)
            {
                return null;
            }

            var organization = await EnsureAsync(organizationId, cancellationToken).ConfigureAwait(false);
            if (organization == null)
            {
                return null;
            }

            var cameras = (await _cameras.GetCamerasAsync(cancellationToken).ConfigureAwait(false)).Count(c => c.OrganizationId == organizationId);
            var storage = (await _gateways.GetAsync(organizationId, cancellationToken).ConfigureAwait(false)).Sum(g => g.Health?.RecordedBytes ?? 0);
            return new BillingStatus(organization, await Plans.FindAsync(organization.PlanId, cancellationToken).ConfigureAwait(false), new BillingUsage(cameras, storage));
        }

        /// <summary>Why one more camera cannot be added (the plan's cameras, an ended subscription); null when it can.</summary>
        public async Task<string> CheckNewCameraAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            var status = await GetAsync(organizationId, cancellationToken).ConfigureAwait(false);
            if (status == null)
            {
                return null;
            }

            if (status.Organization.SubscriptionState == SubscriptionState.Canceled)
            {
                return "Your subscription has ended: choose a plan (Settings › Billing) to add cameras. Cameras you have keep recording.";
            }

            return status.Plan != null && status.Plan.Cameras > 0 && status.Usage.Cameras >= status.Plan.Cameras
                ? $"Your plan ({status.Plan.Name}) includes {status.Plan.Cameras} camera(s): choose a larger plan (Settings › Billing) to add more."
                : null;
        }

        /// <summary>The payment provider's checkout for a plan (owners).</summary>
        public async Task<ServiceResult<string>> CheckoutAsync(string organizationId, string planId, string email, string successUrl, string cancelUrl, CancellationToken cancellationToken = default)
        {
            if (!CanPay)
            {
                return ServiceResult<string>.NotFound();
            }

            var plan = await Plans.FindAsync(planId, cancellationToken).ConfigureAwait(false);
            if (plan == null || !plan.Active || string.IsNullOrWhiteSpace(plan.ProviderPriceId))
            {
                return ServiceResult<string>.Invalid(new[] { "Choose one of the plans." });
            }

            var organization = await EnsureAsync(organizationId, cancellationToken).ConfigureAwait(false);
            if (organization.BillingSubscriptionId != null && organization.SubscriptionState is SubscriptionState.Active or SubscriptionState.PastDue)
            {
                return ServiceResult<string>.Conflict("You have a subscription: change its plan in Manage billing.");
            }

            return ServiceResult<string>.Ok(await _provider.CreateCheckoutAsync(organizationId, plan, organization.BillingCustomerId, email, successUrl, cancelUrl, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>The payment provider's page for the organization's subscription, invoices and card (owners).</summary>
        public async Task<ServiceResult<string>> PortalAsync(string organizationId, string returnUrl, CancellationToken cancellationToken = default)
        {
            var organization = CanPay ? await EnsureAsync(organizationId, cancellationToken).ConfigureAwait(false) : null;
            if (organization?.BillingCustomerId == null)
            {
                return ServiceResult<string>.NotFound();
            }

            return ServiceResult<string>.Ok(await _provider.CreatePortalAsync(organization.BillingCustomerId, returnUrl, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>A webhook from the payment provider; returns the organization it changed (null: none).</summary>
        public async Task<OrganizationRecord> HandleWebhookAsync(string payload, string signature, CancellationToken cancellationToken = default)
        {
            var change = _provider.ParseWebhook(payload, signature, _timeProvider.GetUtcNow());
            return change == null ? null : await ApplyAsync(change, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Applies what the provider said; events can come twice and out of order, so moves the table refuses are skipped.</summary>
        public async Task<OrganizationRecord> ApplyAsync(BillingEvent change, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    var organization = (change.OrganizationId == null ? null : await db.Organizations.FirstOrDefaultAsync(o => o.Id == change.OrganizationId, cancellationToken).ConfigureAwait(false))
                        ?? (change.SubscriptionId == null ? null : await db.Organizations.FirstOrDefaultAsync(o => o.BillingSubscriptionId == change.SubscriptionId, cancellationToken).ConfigureAwait(false))
                        ?? (change.CustomerId == null ? null : await db.Organizations.FirstOrDefaultAsync(o => o.BillingCustomerId == change.CustomerId, cancellationToken).ConfigureAwait(false));
                    if (organization == null)
                    {
                        _logger?.LogWarning("Billing event {Id} ({Kind}) is for no organization here", change.Id, change.Kind);
                        return null;
                    }

                    organization.BillingCustomerId = change.CustomerId ?? organization.BillingCustomerId;
                    organization.BillingSubscriptionId = change.SubscriptionId ?? organization.BillingSubscriptionId;
                    var plan = await Plans.FindByPriceAsync(change.PriceId, cancellationToken).ConfigureAwait(false) ?? await Plans.FindAsync(change.PlanId, cancellationToken).ConfigureAwait(false);
                    organization.PlanId = plan?.Id ?? organization.PlanId;
                    organization.CurrentPeriodEnd = change.PeriodEnd ?? organization.CurrentPeriodEnd;

                    var to = change.Kind == BillingEventKinds.CheckoutCompleted ? SubscriptionState.Active : change.State;
                    var from = organization.SubscriptionState ?? SubscriptionStates.Machine.Initial;
                    if (to is SubscriptionState next && next != from)
                    {
                        if (SubscriptionStates.Machine.CanMove(from, next))
                        {
                            organization.SubscriptionState = SubscriptionStates.Machine.Move(from, next);
                        }
                        else
                        {
                            _logger?.LogInformation("Billing event {Id}: {From} does not become {To}; skipped", change.Id, from, to);
                        }
                    }

                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    _logger?.LogInformation("Organization {Organization}: plan {Plan}, {State}", organization.Id, organization.PlanId, organization.SubscriptionState);
                    return organization;
                }
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Trials past their end without a subscription end. Returns how many.</summary>
        public async Task<int> EndTrialsAsync(CancellationToken cancellationToken = default)
        {
            var now = Now;
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var ended = await db.Organizations.Where(o => o.SubscriptionState == SubscriptionState.Trialing && o.TrialEndsAt != null && o.TrialEndsAt <= now).ToListAsync(cancellationToken).ConfigureAwait(false);
                foreach (var organization in ended)
                {
                    organization.SubscriptionState = SubscriptionStates.Machine.Move(SubscriptionState.Trialing, SubscriptionState.Canceled);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return ended.Count;
            }
        }

        #endregion

        #region Protected and Private Methods

        /// <summary>The configured plans into the database before anything asks for them.</summary>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            if (Options.Enabled)
            {
                await Plans.SeedAsync(cancellationToken).ConfigureAwait(false);
            }

            await base.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!Options.Enabled)
            {
                return;
            }

            using (var timer = new PeriodicTimer(CheckInterval, _timeProvider))
            {
                do
                {
                    try
                    {
                        await EndTrialsAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger?.LogWarning(ex, "Ending trials failed");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
            }
        }

        /// <summary>The organization on its plan: the trial plan for one that has none (new, or made before billing).</summary>
        private async Task<OrganizationRecord> EnsureAsync(string organizationId, CancellationToken cancellationToken)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var organization = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken).ConfigureAwait(false);
                if (organization != null && organization.PlanId == null)
                {
                    Options.StartTrial(organization, Now);
                    organization.PlanId = (await Plans.FindActiveAsync(Options.TrialPlan, cancellationToken).ConfigureAwait(false))?.Id;
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }

                return organization;
            }
        }

        #endregion
    }
}
