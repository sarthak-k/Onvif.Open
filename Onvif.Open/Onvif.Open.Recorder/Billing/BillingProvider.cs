// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Onvif.Open.Recorder.Billing
{
    /// <summary>What the payment provider said happened, in this recorder's terms.</summary>
    /// <param name="State">The subscription's state now (<see cref="SubscriptionStates"/>), when the event says.</param>
    public sealed record BillingEvent(string Id, string Kind, string OrganizationId, string CustomerId, string SubscriptionId, string PriceId, string PlanId, SubscriptionState? State, DateTime? PeriodEnd);

    public static class BillingEventKinds
    {
        public const string CheckoutCompleted = "CheckoutCompleted";
        public const string SubscriptionChanged = "SubscriptionChanged";
        public const string PaymentFailed = "PaymentFailed";
    }

    /// <summary>A webhook that is not the provider's (bad signature, too old).</summary>
    public sealed class BillingSignatureException : Exception
    {
        public BillingSignatureException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Where organizations pay: a checkout for a plan, a portal for their subscription, and its webhooks.</summary>
    public interface IBillingProvider
    {
        bool IsConfigured { get; }

        /// <summary>The address of the provider's checkout page for the plan.</summary>
        Task<string> CreateCheckoutAsync(string organizationId, Plan plan, string customerId, string email, string successUrl, string cancelUrl, CancellationToken cancellationToken = default);

        /// <summary>The address of the provider's page where the customer manages its subscription, invoices and card.</summary>
        Task<string> CreatePortalAsync(string customerId, string returnUrl, CancellationToken cancellationToken = default);

        /// <summary>The event in a webhook, checked to be the provider's; null for events that do not matter here.</summary>
        BillingEvent ParseWebhook(string payload, string signature, DateTimeOffset now);
    }

    /// <summary>
    /// <see cref="IBillingProvider"/> with Stripe's .NET SDK: Checkout sessions in subscription mode, the customer portal,
    /// and webhooks checked by the SDK against the signing secret (at most 5 minutes old). The organization travels as
    /// client_reference_id and in the subscription's metadata.
    /// </summary>
    public sealed class StripeBilling : IBillingProvider
    {
        /// <param name="http">How Stripe is reached (tests give their own).</param>
        public StripeBilling(BillingOptions options, HttpClient http)
        {
            _options = options ?? new BillingOptions();
            _client = _options.Stripe.IsConfigured
                ? new Stripe.StripeClient(_options.Stripe.SecretKey, httpClient: new Stripe.SystemNetHttpClient(http ?? throw new ArgumentNullException(nameof(http))), apiBase: _options.Stripe.ApiUrl)
                : null;
        }

        public static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

        private readonly BillingOptions _options;
        private readonly Stripe.StripeClient _client;

        public bool IsConfigured => _client != null;

        public async Task<string> CreateCheckoutAsync(string organizationId, Plan plan, string customerId, string email, string successUrl, string cancelUrl, CancellationToken cancellationToken = default)
        {
            var metadata = new Dictionary<string, string> { ["organization_id"] = organizationId, ["plan"] = plan.Id };
            var session = await new Stripe.Checkout.SessionService(_client).CreateAsync(new Stripe.Checkout.SessionCreateOptions
            {
                Mode = "subscription",
                LineItems = new List<Stripe.Checkout.SessionLineItemOptions> { new Stripe.Checkout.SessionLineItemOptions { Price = plan.ProviderPriceId, Quantity = 1 } },
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                ClientReferenceId = organizationId,
                Metadata = metadata,
                SubscriptionData = new Stripe.Checkout.SessionSubscriptionDataOptions { Metadata = metadata },
                Customer = customerId,
                CustomerEmail = customerId == null ? email : null
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return session.Url;
        }

        public async Task<string> CreatePortalAsync(string customerId, string returnUrl, CancellationToken cancellationToken = default) =>
            (await new Stripe.BillingPortal.SessionService(_client).CreateAsync(new Stripe.BillingPortal.SessionCreateOptions { Customer = customerId, ReturnUrl = returnUrl }, cancellationToken: cancellationToken).ConfigureAwait(false)).Url;

        public BillingEvent ParseWebhook(string payload, string signature, DateTimeOffset now)
        {
            Stripe.Event stripeEvent;
            try
            {
                stripeEvent = Stripe.EventUtility.ConstructEvent(payload ?? string.Empty, signature ?? string.Empty, _options.Stripe.WebhookSecret, (long)Tolerance.TotalSeconds, throwOnApiVersionMismatch: false);
            }
            catch (Stripe.StripeException ex)
            {
                throw new BillingSignatureException(ex.Message);
            }

            switch (stripeEvent.Data.Object)
            {
                case Stripe.Checkout.Session session when stripeEvent.Type == Stripe.EventTypes.CheckoutSessionCompleted:
                    return new BillingEvent(stripeEvent.Id, BillingEventKinds.CheckoutCompleted, session.ClientReferenceId ?? Metadata(session.Metadata, "organization_id"), session.CustomerId, session.SubscriptionId, null, Metadata(session.Metadata, "plan"), null, null);

                case Stripe.Subscription subscription when stripeEvent.Type is Stripe.EventTypes.CustomerSubscriptionCreated or Stripe.EventTypes.CustomerSubscriptionUpdated or Stripe.EventTypes.CustomerSubscriptionDeleted:
                    var item = subscription.Items?.Data?.FirstOrDefault();
                    var status = stripeEvent.Type == Stripe.EventTypes.CustomerSubscriptionDeleted ? "canceled" : subscription.Status;
                    var end = item?.CurrentPeriodEnd;
                    return new BillingEvent(stripeEvent.Id, BillingEventKinds.SubscriptionChanged, Metadata(subscription.Metadata, "organization_id"), subscription.CustomerId, subscription.Id, item?.Price?.Id, Metadata(subscription.Metadata, "plan"),
                        StateOf(status), end is DateTime at && at > DateTime.UnixEpoch ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null);

                case Stripe.Invoice invoice when stripeEvent.Type == Stripe.EventTypes.InvoicePaymentFailed:
                    return new BillingEvent(stripeEvent.Id, BillingEventKinds.PaymentFailed, null, invoice.CustomerId, invoice.Parent?.SubscriptionDetails?.SubscriptionId, null, null, SubscriptionState.PastDue, null);

                default:
                    return null;
            }
        }

        /// <summary>Stripe's subscription status as this recorder's state; null for ones that change nothing (incomplete).</summary>
        public static SubscriptionState? StateOf(string status) => status switch
        {
            "active" or "trialing" => SubscriptionState.Active,
            "past_due" or "unpaid" => SubscriptionState.PastDue,
            "canceled" or "incomplete_expired" => SubscriptionState.Canceled,
            _ => null
        };

        private static string Metadata(IDictionary<string, string> metadata, string name) =>
            metadata != null && metadata.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    }
}
