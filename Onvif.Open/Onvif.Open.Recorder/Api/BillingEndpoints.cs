// Sarthak Killedar
// 2026-11-11

using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Billing;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: the organization's plan, subscription and usage (administrators see it, owners pay), and the payment
    /// provider's webhook.
    /// </summary>
    internal static class BillingEndpoints
    {
        public sealed class CheckoutRequest
        {
            public string PlanId { get; set; }

            /// <summary>The web app's billing page, to come back to.</summary>
            public string ReturnUrl { get; set; }
        }

        public sealed class PortalRequest
        {
            public string ReturnUrl { get; set; }
        }

        public static IEndpointRouteBuilder MapBillingApi(this IEndpointRouteBuilder app)
        {
            var billing = app.MapGroup("/api/billing");

            billing.MapGet("", async (ClaimsPrincipal user, BillingService service, CancellationToken ct) =>
            {
                var status = await service.GetAsync(user.OrganizationId(), ct);
                return status == null ? Results.Ok(new { enabled = false }) : Results.Ok(ToDto(status, service, await service.Plans.GetActiveAsync(ct)));
            }).RequireAuthorization(Policies.OrganizationAdmin);

            billing.MapPost("/checkout", async (CheckoutRequest request, ClaimsPrincipal user, BillingService service, HttpContext http, SecurityOptions security, CloudOptions cloud, CancellationToken ct) =>
            {
                var back = Back(request?.ReturnUrl, http, security, cloud);
                if (back == null)
                {
                    return Results.Problem("returnUrl must be the web app's billing page.", statusCode: StatusCodes.Status400BadRequest);
                }

                var separator = back.Contains('?') ? "&" : "?";
                return TenancyEndpoints.ToResult(await service.CheckoutAsync(user.OrganizationId(), request?.PlanId, user.Identity?.Name, back + separator + "checkout=done", back, ct), url => new { url });
            })
                .RequireAuthorization(Policies.Owner)
                .Audit(AuditActions.BillingCheckout, AuditTargets.Organization, details: context => new { plan = context.GetArgument<CheckoutRequest>(0)?.PlanId });

            billing.MapPost("/portal", async (PortalRequest request, ClaimsPrincipal user, BillingService service, HttpContext http, SecurityOptions security, CloudOptions cloud, CancellationToken ct) =>
            {
                var back = Back(request?.ReturnUrl, http, security, cloud);
                return back == null
                    ? Results.Problem("returnUrl must be the web app's billing page.", statusCode: StatusCodes.Status400BadRequest)
                    : TenancyEndpoints.ToResult(await service.PortalAsync(user.OrganizationId(), back, ct), url => new { url });
            }).RequireAuthorization(Policies.Owner);

            // Signed by the payment provider; read raw (the signature is over the exact bytes).
            billing.MapPost("/webhook", async (HttpContext http, BillingService service, AuditLog audit, CancellationToken ct) =>
            {
                if (!service.CanPay)
                {
                    return Results.NotFound();
                }

                using var reader = new StreamReader(http.Request.Body);
                var payload = await reader.ReadToEndAsync(ct);
                try
                {
                    var changed = await service.HandleWebhookAsync(payload, http.Request.Headers["Stripe-Signature"].ToString(), ct);
                    if (changed != null)
                    {
                        audit.Record(http, (Member)null, changed.Id, "payment provider", AuditActions.BillingChange, new { plan = changed.PlanId, state = changed.SubscriptionState?.ToString() });
                    }

                    return Results.Ok();
                }
                catch (BillingSignatureException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
                }
            }).AllowAnonymous().DisableAntiforgery();

            // The plans themselves: the operator's owners (Billing:OperatorOrganizationId). Every change is audited, before and after.
            var plans = billing.MapGroup("/plans").RequireAuthorization(Policies.Owner)
                .AddEndpointFilter(async (context, next) =>
                    context.HttpContext.RequestServices.GetRequiredService<BillingService>().IsOperator(context.HttpContext.User.OrganizationId())
                        ? await next(context)
                        : Results.Json(new { errors = new[] { "Plans are managed by the organization that runs this service." } }, statusCode: StatusCodes.Status403Forbidden));

            plans.MapGet("", async (BillingService service, CancellationToken ct) =>
                (await service.Plans.GetAllAsync(ct)).Select(p => ToOperatorDto(p.Plan, p.Organizations)));

            plans.MapPost("", async (PlanRequest request, BillingService service, AuditLog audit, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.Plans.CreateAsync(request, ct);
                if (result.Status != ServiceStatus.Ok)
                {
                    return TenancyEndpoints.ToResult(result);
                }

                audit.Record(http, AuditActions.PlanCreate, AuditTargets.Plan, result.Value.Id, result.Value.Name, new { after = Terms(result.Value) });
                return Results.Created($"/api/billing/plans/{result.Value.Id}", ToOperatorDto(result.Value, 0));
            });

            plans.MapPut("/{id}", async (string id, PlanRequest request, BillingService service, AuditLog audit, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.Plans.UpdateAsync(id, request, ct);
                if (result.Status != ServiceStatus.Ok)
                {
                    return TenancyEndpoints.ToResult(result);
                }

                var (plan, previous) = (result.Value.Plan, result.Value.Previous);
                var versioned = plan.Id != previous.Id;
                audit.Record(http, versioned ? AuditActions.PlanVersion : AuditActions.PlanUpdate, AuditTargets.Plan, plan.Id, plan.Name,
                    new { before = Terms(previous), after = Terms(plan), replaces = versioned ? previous.Id : null });
                return Results.Ok(ToOperatorDto(plan, 0));
            });

            plans.MapPost("/{id}/retire", async (string id, BillingService service, AuditLog audit, HttpContext http, CancellationToken ct) =>
            {
                var result = await service.Plans.RetireAsync(id, ct);
                if (result.Status == ServiceStatus.Ok)
                {
                    audit.Record(http, AuditActions.PlanRetire, AuditTargets.Plan, result.Value.Id, result.Value.Name, new { terms = Terms(result.Value) });
                }

                return TenancyEndpoints.ToResult(result, p => ToOperatorDto(p, 0));
            });

            return app;
        }

        /// <summary>What a plan sells, for the audit log.</summary>
        private static object Terms(Store.PlanRecord plan) => new
        {
            code = plan.Code,
            version = plan.Version,
            name = plan.Name,
            cameras = plan.Cameras,
            storageGB = plan.StorageGB,
            monthlyPrice = plan.MonthlyPrice,
            providerPriceId = plan.ProviderPriceId
        };

        private static object ToOperatorDto(Store.PlanRecord plan, int organizations) => new
        {
            id = plan.Id,
            code = plan.Code,
            version = plan.Version,
            name = plan.Name,
            cameras = plan.Cameras,
            storageGB = plan.StorageGB,
            monthlyPrice = plan.MonthlyPrice,
            providerPriceId = plan.ProviderPriceId,
            sortOrder = plan.SortOrder,
            active = plan.Active,
            replacedById = plan.ReplacedById,
            retiredAt = Utc(plan.RetiredAt),
            organizations
        };

        private static string Back(string returnUrl, HttpContext http, SecurityOptions security, CloudOptions cloud) =>
            SsoEndpoints.WebAppOf(returnUrl, http, security, cloud) is string origin && Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
                ? origin + uri.AbsolutePath
                : null;

        private static object ToDto(BillingStatus status, BillingService service, System.Collections.Generic.IReadOnlyList<Store.PlanRecord> offered)
        {
            var organization = status.Organization;
            var options = service.Options;
            return new
            {
                enabled = true,
                currency = options.Currency,
                plan = status.Plan == null ? null : ToDto(status.Plan),
                state = organization.SubscriptionState?.ToString(),
                trialEndsAt = Utc(organization.TrialEndsAt),
                currentPeriodEnd = Utc(organization.CurrentPeriodEnd),
                usage = new { cameras = status.Usage.Cameras, storageBytes = status.Usage.StorageBytes },
                overCameras = status.OverCameras,
                overStorage = status.OverStorage,
                canPay = service.CanPay,
                canManage = service.CanPay && organization.BillingCustomerId != null,
                salesEmail = options.SalesEmail,
                plans = offered.Select(ToDto)
            };
        }

        private static object ToDto(Store.PlanRecord plan) => new
        {
            id = plan.Id,
            code = plan.Code,
            name = plan.Name,
            active = plan.Active,
            cameras = plan.Cameras,
            storageGB = plan.StorageGB,
            monthlyPrice = plan.MonthlyPrice,
            purchasable = !string.IsNullOrWhiteSpace(plan.ProviderPriceId)
        };

        private static DateTime? Utc(DateTime? value) => value is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : null;
    }
}
