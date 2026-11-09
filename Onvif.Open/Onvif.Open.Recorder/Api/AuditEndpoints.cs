// Sarthak Killedar
// 2026-11-09

using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: the organization's audit log, newest first (administrators who see all sites).
    /// </summary>
    internal static class AuditEndpoints
    {
        public static IEndpointRouteBuilder MapAuditApi(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/audit", async (ClaimsPrincipal user, IAuditStore store, string userId, string action, DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset? before, int? limit, CancellationToken ct) =>
            {
                var entries = await store.QueryAsync(new AuditQuery
                {
                    OrganizationId = user.OrganizationId(),
                    UserId = userId,
                    Action = action,
                    From = from?.UtcDateTime,
                    To = to?.UtcDateTime,
                    Before = before?.UtcDateTime,
                    Limit = Math.Clamp(limit ?? 100, 1, 1000)
                }, ct);

                return entries.Select(a => new
                {
                    id = a.Id,
                    time = new DateTimeOffset(DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc)),
                    userId = a.UserId,
                    userName = a.UserName,
                    action = a.Action,
                    targetType = a.TargetType,
                    targetId = a.TargetId,
                    targetName = a.TargetName,
                    details = a.Details == null ? (JsonElement?)null : JsonDocument.Parse(a.Details).RootElement,
                    ipAddress = a.IpAddress
                });
            }).RequireAuthorization(Policies.OrganizationAdmin);

            return app;
        }
    }
}
