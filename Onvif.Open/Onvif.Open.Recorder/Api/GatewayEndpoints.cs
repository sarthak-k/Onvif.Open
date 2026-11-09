// Sarthak Killedar
// 2026-11-09

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: the organization's gateways (what records the cameras), their health, and their settings: storage
    /// limits, cloud backup and discovery. Everyone sees the gateways; administrators who see every site change them.
    /// Backup settings are write-only (they hold storage keys): responses list their names.
    /// </summary>
    internal static class GatewayEndpoints
    {
        #region Public Methods

        public static IEndpointRouteBuilder MapGatewayApi(this IEndpointRouteBuilder app)
        {
            var gateways = app.MapGroup("/api/gateways").RequireAuthorization(Policies.Viewer);

            gateways.MapGet("", async (ClaimsPrincipal user, GatewayService service, CancellationToken ct) =>
            {
                await service.EnsureLocalAsync(ct);
                return (await service.GetAsync(user.OrganizationId(), ct)).Select(g => ToDto(g, service, IsOrganizationAdmin(user)));
            });

            gateways.MapGet("/{id}", async (string id, ClaimsPrincipal user, GatewayService service, CancellationToken ct) =>
            {
                var gateway = await service.FindAsync(user.OrganizationId(), id, ct);
                return gateway == null ? Results.NotFound() : Results.Ok(ToDto(gateway, service, IsOrganizationAdmin(user)));
            });

            gateways.MapPut("/{id}", async (string id, GatewayRequest request, ClaimsPrincipal user, GatewayService service, CancellationToken ct) =>
                TenancyEndpoints.ToResult(await service.UpdateAsync(user.OrganizationId(), id, request, ct), g => ToDto(g, service, true)))
                .RequireAuthorization(Policies.OrganizationAdmin)
                .Audit(AuditActions.GatewayUpdate, AuditTargets.Gateway, details: context => Changed(context.GetArgument<GatewayRequest>(1)));

            return app;
        }

        #endregion

        #region Private Methods

        private static bool IsOrganizationAdmin(ClaimsPrincipal user) => Roles.AtLeast(Roles.Admin).Any(user.IsInRole) && user.HasAllSites();

        /// <summary>Which settings a change touched (never their values: backup settings are secrets).</summary>
        private static object Changed(GatewayRequest request) => new
        {
            name = request?.Name != null,
            storage = request?.Storage != null,
            backup = request?.Backup?.Provider is string provider && provider.Length > 0 ? provider : request?.Backup != null ? "none" : null,
            discovery = request?.Discovery != null
        };

        /// <param name="settings">Whether to include the settings (administrators): who sees all of them may change them.</param>
        private static object ToDto(Gateway gateway, GatewayService service, bool settings)
        {
            var online = service.IsOnline(gateway);
            return new
            {
                id = gateway.Id,
                name = gateway.Name,
                location = gateway.Location,
                embedded = gateway.Embedded,
                status = online ? "Online" : gateway.LastSeenAt == null ? "Waiting" : "Offline",
                version = gateway.Version,
                startedAt = gateway.StartedAt,
                lastSeenAt = gateway.LastSeenAt,
                health = gateway.Health,
                restartRequired = service.IsRestartRequired(gateway),
                storage = settings ? gateway.Storage : null,
                backup = settings
                    ? new
                    {
                        provider = gateway.Backup.Provider,
                        prefix = gateway.Backup.Prefix,
                        keepLocalCopyHours = gateway.Backup.KeepLocalCopyHours,
                        settings = (gateway.Backup.Settings ?? new()).Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    }
                    : null,
                discovery = settings ? gateway.Discovery : null
            };
        }

        #endregion
    }
}
