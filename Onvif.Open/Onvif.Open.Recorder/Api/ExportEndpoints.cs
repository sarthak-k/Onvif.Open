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
using Onvif.Open.Recorder.Exports;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: watermarked MP4 exports (everyone, for the cameras they see) and share links to them (operators and
    /// above). A share link downloads its export without signing in until it expires or is revoked; every download is
    /// in the audit log.
    /// </summary>
    internal static class ExportEndpoints
    {
        #region Public Variables

        public sealed class LinkRequest
        {
            /// <summary>How long the link works (1 to 168 hours; default 72).</summary>
            public int Hours { get; set; } = 72;
        }

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapExportApi(this IEndpointRouteBuilder app)
        {
            var exports = app.MapGroup("/api/exports").RequireAuthorization(Policies.Viewer);

            exports.MapGet("", async (ClaimsPrincipal user, ExportService service, CancellationToken ct) =>
                (await service.GetAsync(user, ct)).Select(e => ToDto(e, service)));

            exports.MapGet("/{id}", async (string id, ClaimsPrincipal user, ExportService service, CancellationToken ct) =>
            {
                var export = await service.FindAsync(user, id, ct);
                return export == null ? Results.NotFound() : Results.Ok(ToDto(export, service));
            });

            exports.MapPost("", async (ExportRequest request, ClaimsPrincipal user, ExportService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(user, request, ct);
                return result.Status == ServiceStatus.Ok ? Results.Accepted($"/api/exports/{result.Value.Id}", ToDto(result.Value, service)) : TenancyEndpoints.ToResult(result);
            })
                .Audit(AuditActions.VideoExport, AuditTargets.Export, details: context =>
                {
                    var request = context.GetArgument<ExportRequest>(0);
                    return new { format = "mp4", camera = request?.CameraId, from = request?.From, to = request?.To, caseId = request?.CaseId };
                });

            exports.MapGet("/{id}/video.mp4", async (string id, ClaimsPrincipal user, ExportService service, CancellationToken ct) =>
            {
                var export = await service.FindAsync(user, id, ct);
                var file = service.FileOf(export);
                return file == null ? Results.NotFound() : Results.File(file, "video/mp4", ExportService.FileName(export), enableRangeProcessing: true);
            });

            exports.MapGet("/{id}/links", async (string id, ClaimsPrincipal user, ExportService service, CancellationToken ct) =>
            {
                var export = await service.FindAsync(user, id, ct);
                return export == null ? Results.NotFound() : Results.Ok((await service.GetLinksAsync(export, ct)).Select(l => ToDto(l, null)));
            }).RequireAuthorization(Policies.Operator);

            exports.MapPost("/{id}/links", async (string id, LinkRequest request, ClaimsPrincipal user, ExportService service, CancellationToken ct) =>
            {
                var export = await service.FindAsync(user, id, ct);
                if (export == null)
                {
                    return Results.NotFound();
                }

                var result = await service.CreateLinkAsync(user, export, TimeSpan.FromHours(request?.Hours ?? 72), ct);
                return result.Status == ServiceStatus.Ok
                    ? Results.Created($"/api/exports/{id}/links/{result.Value.Link.Id}", ToDto(result.Value.Link, result.Value.Token))
                    : TenancyEndpoints.ToResult(result);
            })
                .RequireAuthorization(Policies.Operator)
                .Audit(AuditActions.VideoShare, AuditTargets.Export, details: context => new { hours = context.GetArgument<LinkRequest>(1)?.Hours });

            exports.MapDelete("/{id}/links/{linkId}", async (string id, string linkId, ClaimsPrincipal user, ExportService service, CancellationToken ct) =>
            {
                var export = await service.FindAsync(user, id, ct);
                if (export == null)
                {
                    return Results.NotFound();
                }

                var result = await service.RevokeLinkAsync(export, linkId, ct);
                return result.Status == ServiceStatus.Ok ? Results.NoContent() : TenancyEndpoints.ToResult(result);
            })
                .RequireAuthorization(Policies.Operator)
                .Audit(AuditActions.VideoShareRevoke, AuditTargets.Export);

            // Anyone with the link, until it expires or is revoked.
            app.MapGet("/api/shared/{token}/video.mp4", async (string token, HttpContext http, ExportService service, AuditLog audit, CancellationToken ct) =>
            {
                var (export, link) = await service.OpenSharedAsync(token, ct);
                if (export == null)
                {
                    return Results.NotFound();
                }

                audit.RecordShared(http, export.OrganizationId, AuditActions.VideoSharedDownload, AuditTargets.Export, export.Id, export.CameraName, new { link = link.Id, downloads = link.Downloads });
                return Results.File(service.FileOf(export), "video/mp4", ExportService.FileName(export), enableRangeProcessing: true);
            }).AllowAnonymous();

            return app;
        }

        #endregion

        #region Private Methods

        private static object ToDto(ExportRecord export, ExportService service) => new
        {
            id = export.Id,
            cameraId = export.CameraId,
            cameraName = export.CameraName,
            siteId = export.SiteId,
            caseId = export.CaseId,
            from = DateTime.SpecifyKind(export.From, DateTimeKind.Utc),
            to = DateTime.SpecifyKind(export.To, DateTimeKind.Utc),
            status = export.Status,
            error = export.Error,
            size = export.Size,
            sha256 = export.Sha256,
            requestedBy = export.RequestedByName,
            createdAt = export.CreatedAt,
            completedAt = export.CompletedAt,
            expiresAt = export.ExpiresAt,
            downloadable = service.FileOf(export) != null
        };

        /// <param name="token">Only when the link was just created: the path then carries it.</param>
        private static object ToDto(ShareLinkRecord link, string token) => new
        {
            id = link.Id,
            path = token == null ? null : $"/api/shared/{token}/video.mp4",
            createdBy = link.CreatedByName,
            createdAt = link.CreatedAt,
            expiresAt = link.ExpiresAt,
            revokedAt = link.RevokedAt,
            downloads = link.Downloads
        };

        #endregion
    }
}
