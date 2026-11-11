// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Recorder.Billing
{
    /// <summary>
    /// "Billing": plans tiered by how many cameras an organization records and how much video it keeps, all in the
    /// configuration (nothing about prices is in the code). New organizations start on <see cref="TrialPlan"/> for
    /// <see cref="TrialDays"/>; paying goes through the payment provider (Stripe), whose price each plan names.
    /// </summary>
    public sealed class BillingOptions
    {
        public const string SectionName = "Billing";

        public bool Enabled { get; set; }

        /// <summary>ISO 4217, as prices are shown (e.g. "USD").</summary>
        public string Currency { get; set; } = "USD";

        /// <summary>From the smallest to the largest.</summary>
        public List<Plan> Plans { get; set; } = new List<Plan>();

        /// <summary>The plan new organizations try; one of <see cref="Plans"/>.</summary>
        public string TrialPlan { get; set; }

        public int TrialDays { get; set; } = 14;

        /// <summary>Shown when there is no payment provider: who to write to for a plan.</summary>
        public string SalesEmail { get; set; }

        public StripeOptions Stripe { get; set; } = new StripeOptions();

        /// <summary>A new organization's start: the trial plan, for <see cref="TrialDays"/> (nothing where billing is off).</summary>
        public void StartTrial(Store.OrganizationRecord organization, DateTime now)
        {
            if (Enabled && organization.PlanId == null)
            {
                organization.PlanId = FindPlan(TrialPlan)?.Id;
                organization.SubscriptionState = SubscriptionStates.Machine.Initial;
                organization.TrialEndsAt = now.AddDays(TrialDays);
            }
        }

        public Plan FindPlan(string id) => Plans.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>The plan whose provider price this is, or null.</summary>
        public Plan FindPlanByPrice(string priceId) => priceId == null ? null : Plans.FirstOrDefault(p => p.ProviderPriceId == priceId);

        /// <summary>Problems with the configuration (empty when valid, or billing is off).</summary>
        public IReadOnlyList<string> Check()
        {
            var errors = new List<string>();
            if (!Enabled)
            {
                return errors;
            }

            if (Plans.Count == 0)
            {
                errors.Add("Billing:Plans needs at least one plan.");
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

            if (FindPlan(TrialPlan) == null)
            {
                errors.Add("Billing:TrialPlan must be one of Billing:Plans.");
            }

            if (TrialDays < 0)
            {
                errors.Add("Billing:TrialDays cannot be negative.");
            }

            if (Stripe.IsConfigured && Plans.Any(p => string.IsNullOrWhiteSpace(p.ProviderPriceId) && p.Id != TrialPlan))
            {
                errors.Add("Billing:Plans: with Stripe, every plan but the trial needs its ProviderPriceId (price_...).");
            }

            return errors;
        }
    }

    /// <summary>A tier: what it includes, and what it costs a month.</summary>
    public sealed class Plan
    {
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
