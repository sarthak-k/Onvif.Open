// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Recorder.Billing
{
    /// <summary>
    /// "Billing": plans tiered by how many cameras an organization records and how much video it keeps. Plans live in
    /// the database (oo_plans), managed by the owners of <see cref="OperatorOrganizationId"/>; <see cref="Plans"/> only
    /// seeds it on first start (a code already there is never overwritten). New organizations start on
    /// <see cref="TrialPlan"/> for <see cref="TrialDays"/>; paying goes through the payment provider (Stripe).
    /// </summary>
    public sealed class BillingOptions
    {
        public const string SectionName = "Billing";

        public bool Enabled { get; set; }

        /// <summary>ISO 4217, as prices are shown (e.g. "USD").</summary>
        public string Currency { get; set; } = "USD";

        /// <summary>Plans to put in the database when their code is not there yet, from the smallest to the largest.</summary>
        public List<PlanSeed> Plans { get; set; } = new List<PlanSeed>();

        /// <summary>The code of the plan new organizations try (an active plan with it must exist).</summary>
        public string TrialPlan { get; set; }

        /// <summary>The organization that runs this service: its owners manage the plans.</summary>
        public string OperatorOrganizationId { get; set; }

        public int TrialDays { get; set; } = 14;

        /// <summary>Shown when there is no payment provider: who to write to for a plan.</summary>
        public string SalesEmail { get; set; }

        public StripeOptions Stripe { get; set; } = new StripeOptions();

        /// <summary>
        /// A new organization's start: its trial, for <see cref="TrialDays"/> from now (nothing where billing is off).
        /// The trial plan itself is set the first time billing looks at the organization.
        /// </summary>
        public void StartTrial(Store.OrganizationRecord organization, DateTime now)
        {
            if (Enabled && organization.SubscriptionState == null)
            {
                organization.SubscriptionState = SubscriptionStates.Machine.Initial;
                organization.TrialEndsAt = now.AddDays(TrialDays);
            }
        }

        /// <summary>Problems with the configuration (empty when valid, or billing is off).</summary>
        public IReadOnlyList<string> Check()
        {
            var errors = new List<string>();
            if (!Enabled)
            {
                return errors;
            }

            foreach (var plan in Plans)
            {
                if (string.IsNullOrWhiteSpace(plan.Id) || string.IsNullOrWhiteSpace(plan.Name) || plan.Cameras < 0 || plan.StorageGB < 0 || plan.MonthlyPrice < 0)
                {
                    errors.Add($"Billing:Plans: '{plan.Id}' needs an Id, a Name, and Cameras, StorageGB and MonthlyPrice of 0 or more.");
                }
            }

            if (Plans.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            {
                errors.Add("Billing:Plans: plan ids must differ.");
            }

            if (string.IsNullOrWhiteSpace(TrialPlan))
            {
                errors.Add("Billing:TrialPlan must be the code of the trial plan.");
            }

            if (TrialDays < 0)
            {
                errors.Add("Billing:TrialDays cannot be negative.");
            }

            if (Stripe.IsConfigured && Plans.Any(p => string.IsNullOrWhiteSpace(p.ProviderPriceId) && p.MonthlyPrice > 0))
            {
                errors.Add("Billing:Plans: with Stripe, every paid plan needs its ProviderPriceId (price_...).");
            }

            return errors;
        }
    }

    /// <summary>A plan to seed the database with: what it includes, and what it costs a month.</summary>
    public sealed class PlanSeed
    {
        /// <summary>The plan's code (stays across its versions).</summary>
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>How many cameras it records; 0: no limit.</summary>
        public int Cameras { get; set; }

        /// <summary>How much recorded video it keeps, in GB; 0: no limit.</summary>
        public double StorageGB { get; set; }

        public decimal MonthlyPrice { get; set; }

        /// <summary>The payment provider's recurring price (Stripe: price_...).</summary>
        public string ProviderPriceId { get; set; }
    }

    public sealed class StripeOptions
    {
        /// <summary>sk_... (keep it out of files: Billing__Stripe__SecretKey).</summary>
        public string SecretKey { get; set; }

        /// <summary>whsec_...: webhooks are signed with it.</summary>
        public string WebhookSecret { get; set; }

        public string ApiUrl { get; set; } = "https://api.stripe.com";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(WebhookSecret);
    }
}
