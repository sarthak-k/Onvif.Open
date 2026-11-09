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
        #region Public Variables

        /// <summary>The header a waiting gateway proves it asked for the code with.</summary>
        public const string PairingSecretHeader = "X-Pairing-Secret";

        /// <summary>The header a paired gateway sends its token in.</summary>
        public const string GatewayTokenHeader = "X-Gateway-Token";

        public sealed class PairingRequest
        {
            public string Name { get; set; }
            public string Location { get; set; }
        }

        public sealed class PairRequest
        {
            public string Code { get; set; }

            /// <summary>Null: the name the gateway gave.</summary>
            public string Name { get; set; }
        }

        public sealed class HeartbeatRequest
        {
            public string Version { get; set; }
            public DateTimeOffset? StartedAt { get; set; }
            public GatewayHealth Health { get; set; }
        }

        #endregion

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

            // Pairing a gateway on another machine: it asks for a code and shows it, an administrator enters it here.
            gateways.MapPost("/pairing", (PairingRequest request, GatewayPairing pairing) =>
            {
                var ticket = pairing.Start(request?.Name, request?.Location);
                return ticket == null
                    ? Results.Json(new { errors = new[] { "Too many gateways are waiting to be paired: try again in a few minutes." } }, statusCode: StatusCodes.Status503ServiceUnavailable)
                    : Results.Created($"/api/gateways/pairing/{ticket.Id}", new { id = ticket.Id, code = ticket.Code, secret = ticket.Secret, expiresAt = ticket.ExpiresAt });
            }).AllowAnonymous().RequireRateLimiting(SecurityConfiguration.SignInLimit);

            gateways.MapGet("/pairing/{id}", (string id, HttpRequest http, GatewayPairing pairing) =>
            {
                var status = pairing.Poll(id, http.Headers[PairingSecretHeader].ToString());
                return status == null
                    ? Results.NotFound()
                    : Results.Ok(new { state = status.State, expiresAt = status.ExpiresAt, gatewayId = status.GatewayId, token = status.Token });
            }).AllowAnonymous();

            gateways.MapPost("/pair", async (PairRequest request, ClaimsPrincipal user, GatewayPairing pairing, GatewayService service, CancellationToken ct) =>
            {
                var result = await pairing.PairAsync(user.OrganizationId(), request?.Code, request?.Name, ct);
                return result.Status == ServiceStatus.Ok
                    ? Results.Created($"/api/gateways/{result.Value.Id}", ToDto(result.Value, service, true))
                    : TenancyEndpoints.ToResult(result);
            })
                .RequireAuthorization(Policies.OrganizationAdmin)
                .RequireRateLimiting(SecurityConfiguration.SignInLimit)
                .Audit(AuditActions.GatewayPair, AuditTargets.Gateway);

            // A paired gateway reports its health with its token.
            gateways.MapPost("/heartbeat", async (HeartbeatRequest request, HttpRequest http, GatewayService service, CancellationToken ct) =>
            {
                var gateway = await service.AuthenticateAsync(http.Headers[GatewayTokenHeader].ToString(), ct);
                if (gateway == null)
                {
                    return Results.Unauthorized();
                }

                await service.ReportAsync(gateway, request?.Version, request?.StartedAt?.UtcDateTime ?? DateTime.UtcNow, request?.Health, ct);
                return Results.Ok(new { id = gateway.Id, name = gateway.Name });
            }).AllowAnonymous();

            gateways.MapDelete("/{id}", async (string id, ClaimsPrincipal user, GatewayService service, Store.ISettingsStore cameras, CancellationToken ct) =>
            {
                var recorded = (await cameras.GetCamerasAsync(ct)).Count(c => string.Equals(c.GatewayId, id, StringComparison.OrdinalIgnoreCase));
                var result = await service.RemoveAsync(user.OrganizationId(), id, recorded, ct);
                return result.Status == ServiceStatus.Ok ? Results.NoContent() : TenancyEndpoints.ToResult(result);
            })
                .RequireAuthorization(Policies.OrganizationAdmin)
                .Audit(AuditActions.GatewayRemove, AuditTargets.Gateway);

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
