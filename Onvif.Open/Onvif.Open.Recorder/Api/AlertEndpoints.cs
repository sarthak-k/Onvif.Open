// Sarthak Killedar
// 2026-11-09

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Alerts;
using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: alert rules (organization administrators) and the alerts inbox (everyone sees the alerts of their
    /// sites; operators and above acknowledge them).
    /// </summary>
    internal static class AlertEndpoints
    {
        #region Public Methods

        public static IEndpointRouteBuilder MapAlertApi(this IEndpointRouteBuilder app)
        {
            var rules = app.MapGroup("/api/alert-rules").RequireAuthorization(Policies.OrganizationAdmin);

            rules.MapGet("", async (ClaimsPrincipal user, AlertService service, CancellationToken ct) =>
                (await service.GetRulesAsync(user, ct)).Select(ToDto));

            rules.MapGet("/{id}", async (string id, ClaimsPrincipal user, AlertService service, CancellationToken ct) =>
                await service.FindRuleAsync(user, id, ct) is AlertRule rule ? Results.Ok(ToDto(rule)) : Results.NotFound());

            rules.MapPost("", async (AlertRuleRequest request, ClaimsPrincipal user, AlertService service, CancellationToken ct) =>
            {
                var result = await service.CreateRuleAsync(user, request, ct);
                return result.Status == ServiceStatus.Ok ? Results.Created($"/api/alert-rules/{result.Value.Id}", ToDto(result.Value)) : TenancyEndpoints.ToResult(result);
            })
                .Audit(AuditActions.AlertRuleCreate, AuditTargets.AlertRule);

            rules.MapPut("/{id}", async (string id, AlertRuleRequest request, ClaimsPrincipal user, AlertService service, CancellationToken ct) =>
                TenancyEndpoints.ToResult(await service.UpdateRuleAsync(user, id, request, ct), ToDto))
                .Audit(AuditActions.AlertRuleUpdate, AuditTargets.AlertRule, details: context => new { enabled = context.GetArgument<AlertRuleRequest>(1)?.Enabled });

            rules.MapDelete("/{id}", async (string id, ClaimsPrincipal user, AlertService service, CancellationToken ct) =>
            {
                var result = await service.DeleteRuleAsync(user, id, ct);
                return result.Status == ServiceStatus.Ok ? Results.NoContent() : TenancyEndpoints.ToResult(result);
            })
                .Audit(AuditActions.AlertRuleDelete, AuditTargets.AlertRule);

            rules.MapPost("/{id}/test", async (string id, ClaimsPrincipal user, AlertService service, CancellationToken ct) =>
                TenancyEndpoints.ToResult(await service.TestRuleAsync(user, id, ct), delivery => new { delivery }));

            var alerts = app.MapGroup("/api/alerts").RequireAuthorization(Policies.Viewer);

            alerts.MapGet("", async (ClaimsPrincipal user, AlertService service, CancellationToken ct, string status = null) =>
                (await service.GetAlertsAsync(user, string.Equals(status, "open", StringComparison.OrdinalIgnoreCase), ct)).Select(ToDto));

            alerts.MapGet("/summary", async (ClaimsPrincipal user, AlertService service, CancellationToken ct) =>
            {
                var summary = await service.SummaryAsync(user, ct);
                return new { open = summary.Open, latest = summary.Latest == null ? null : ToDto(summary.Latest) };
            });

            alerts.MapPost("/{id}/acknowledge", async (string id, ClaimsPrincipal user, AlertService service, CancellationToken ct) =>
                TenancyEndpoints.ToResult(await service.AcknowledgeAsync(user, id, ct), ToDto))
                .RequireAuthorization(Policies.Operator)
                .Audit(AuditActions.AlertAcknowledge, AuditTargets.Alert);

            return app;
        }

        #endregion

        #region Private Methods

        private static object ToDto(AlertRule rule) => new
        {
            id = rule.Id,
            name = rule.Name,
            enabled = rule.Enabled,
            kind = rule.Kind,
            categories = rule.Categories,
            minSeverity = rule.MinSeverity,
            offlineMinutes = rule.OfflineMinutes,
            siteIds = rule.SiteIds,
            cameraIds = rule.CameraIds,
            hours = rule.Hours,
            cooldownMinutes = rule.CooldownMinutes,
            emails = rule.Emails,
            webhookUrl = rule.WebhookUrl,
            hasWebhookSecret = !string.IsNullOrEmpty(rule.WebhookSecret),
            createdAt = rule.CreatedAt,
            updatedAt = rule.UpdatedAt
        };

        private static object ToDto(AlertRecord alert) => new
        {
            id = alert.Id,
            ruleId = alert.RuleId,
            ruleName = alert.RuleName,
            siteId = alert.SiteId,
            cameraId = alert.CameraId,
            cameraName = alert.CameraName,
            eventId = alert.EventId,
            category = alert.Category,
            severity = alert.Severity,
            title = alert.Title,
            triggeredAt = DateTime.SpecifyKind(alert.TriggeredAt, DateTimeKind.Utc),
            acknowledgedAt = alert.AcknowledgedAt is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc) : (DateTime?)null,
            acknowledgedBy = alert.AcknowledgedBy,
            delivery = AlertDelivery.Read(alert.Delivery)
        };

        #endregion
    }
}
