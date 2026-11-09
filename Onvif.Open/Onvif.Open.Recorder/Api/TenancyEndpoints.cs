// Sarthak Killedar
// 2026-11-09

using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: first-run setup, the organization, its sites and its recording policies.
    /// </summary>
    internal static class TenancyEndpoints
    {
        #region Public Methods

        public static IEndpointRouteBuilder MapSetupApi(this IEndpointRouteBuilder app)
        {
            // Whether the recorder still needs its first owner, and what setup offers.
            app.MapGet("/api/setup", async (TenancyService tenancy, SecurityOptions security, CancellationToken ct) => new
            {
                required = await tenancy.IsSetupRequiredAsync(ct),
                tokenRequired = !string.IsNullOrEmpty(security.SetupToken),
                regions = Regions.All.Select(r => new { id = r.Id, name = r.Name })
            }).AllowAnonymous();

            // Creates the organization, first site, default policies and owner, and signs the owner in.
            app.MapPost("/api/setup", async (SetupRequest request, TenancyService tenancy, Gateways.GatewayHeartbeat heartbeat, AuditLog audit, HttpContext http, CancellationToken ct) =>
            {
                var result = await tenancy.CompleteSetupAsync(request, ct);
                if (result.Status == ServiceStatus.Ok)
                {
                    audit.Record(http, result.Value, null, null, AuditActions.Setup, new { region = request.Region });

                    // The recorder's gateway exists from now on, with its first heartbeat.
                    await heartbeat.ReportNowAsync(ct);
                }

                return result.Status == ServiceStatus.Ok ? AuthEndpoints.SignIn(result.Value) : ToResult(result);
            }).AllowAnonymous().RequireRateLimiting(SecurityConfiguration.SignInLimit);

            return app;
        }

        public static IEndpointRouteBuilder MapTenancyApi(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/organization", async (ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
            {
                var organization = await tenancy.GetOrganizationAsync(user.OrganizationId(), ct);
                return organization == null ? Results.NotFound() : Results.Ok(ToDto(organization));
            });

            app.MapPut("/api/organization", async (OrganizationRequest request, ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
                ToResult(await tenancy.RenameOrganizationAsync(user.OrganizationId(), request?.Name, ct), ToDto))
                .RequireAuthorization(Policies.Owner)
                .Audit(AuditActions.OrganizationRename, AuditTargets.Organization);

            // Every signed-in user sees the sites they may see; administrators who see all sites manage them.
            var sites = app.MapGroup("/api/sites");

            sites.MapGet("", async (ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
                (await tenancy.GetSitesAsync(user.OrganizationId(), user.Sites(), ct)).Select(ToDto));

            sites.MapPost("", async (SiteRequest request, ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
            {
                var result = await tenancy.CreateSiteAsync(user.OrganizationId(), request, ct);
                return result.Status == ServiceStatus.Ok ? Results.Created($"/api/sites/{result.Value.Id}", ToDto(result.Value)) : ToResult(result);
            }).RequireAuthorization(Policies.OrganizationAdmin)
                .Audit(AuditActions.SiteCreate, AuditTargets.Site);

            sites.MapPut("/{id}", async (string id, SiteRequest request, ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
                ToResult(await tenancy.UpdateSiteAsync(user.OrganizationId(), id, request, ct), ToDto))
                .RequireAuthorization(Policies.OrganizationAdmin)
                .Audit(AuditActions.SiteUpdate, AuditTargets.Site);

            sites.MapDelete("/{id}", async (string id, ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
            {
                var result = await tenancy.DeleteSiteAsync(user.OrganizationId(), id, ct);
                return result.Status == ServiceStatus.Ok ? Results.NoContent() : ToResult(result);
            }).RequireAuthorization(Policies.OrganizationAdmin)
                .Audit(AuditActions.SiteDelete, AuditTargets.Site);

            // Recording policies: administrators choose them for cameras; organization administrators change them.
            var policies = app.MapGroup("/api/recording-policies").RequireAuthorization(Policies.Admin);

            policies.MapGet("", async (ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
                (await tenancy.GetPoliciesAsync(user.OrganizationId(), ct)).Select(ToDto));

            policies.MapPost("", async (PolicyRequest request, ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
            {
                var result = await tenancy.CreatePolicyAsync(user.OrganizationId(), request, ct);
                return result.Status == ServiceStatus.Ok ? Results.Created($"/api/recording-policies/{result.Value.Id}", ToDto(result.Value)) : ToResult(result);
            }).RequireAuthorization(Policies.OrganizationAdmin)
                .Audit(AuditActions.PolicyCreate, AuditTargets.Policy);

            policies.MapPut("/{id}", async (string id, PolicyRequest request, ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
                ToResult(await tenancy.UpdatePolicyAsync(user.OrganizationId(), id, request, ct), ToDto))
                .RequireAuthorization(Policies.OrganizationAdmin)
                .Audit(AuditActions.PolicyUpdate, AuditTargets.Policy);

            policies.MapDelete("/{id}", async (string id, ClaimsPrincipal user, TenancyService tenancy, CancellationToken ct) =>
            {
                var result = await tenancy.DeletePolicyAsync(user.OrganizationId(), id, ct);
                return result.Status == ServiceStatus.Ok ? Results.NoContent() : ToResult(result);
            }).RequireAuthorization(Policies.OrganizationAdmin)
                .Audit(AuditActions.PolicyDelete, AuditTargets.Policy);

            return app;
        }

        #endregion

        #region Private Methods

        private static object ToDto(OrganizationRecord organization) => new { id = organization.Id, name = organization.Name, region = organization.Region };

        private static object ToDto(SiteRecord site) => new { id = site.Id, name = site.Name, timeZone = site.TimeZone, address = site.Address };

        private static object ToDto(RecordingPolicyRecord policy) => new
        {
            id = policy.Id,
            name = policy.Name,
            mode = policy.Mode,
            preRollSeconds = policy.PreRollSeconds,
            postRollSeconds = policy.PostRollSeconds,
            continuousRetentionDays = policy.ContinuousRetentionDays,
            eventRetentionDays = policy.EventRetentionDays,
            isDefault = policy.IsDefault
        };

        internal static IResult ToResult<T>(ServiceResult<T> result, System.Func<T, object> toDto)
        {
            return result.Status == ServiceStatus.Ok ? Results.Ok(toDto(result.Value)) : ToResult(result);
        }

        internal static IResult ToResult<T>(ServiceResult<T> result)
        {
            switch (result.Status)
            {
                case ServiceStatus.NotFound:
                    return Results.NotFound();
                case ServiceStatus.Conflict:
                    return Results.Conflict(new { errors = result.Errors });
                case ServiceStatus.Forbidden:
                    return Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status403Forbidden);
                default:
                    return Results.BadRequest(new { errors = result.Errors });
            }
        }

        #endregion

        public sealed class OrganizationRequest
        {
            public string Name { get; set; }
        }
    }
}
