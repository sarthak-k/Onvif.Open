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
using Onvif.Open.Recorder.Cases;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: cases and their clips (bookmarks with notes). Everyone reads them; operators and above open cases and
    /// add, annotate and remove clips; administrators delete cases. Clips are shown to users who see their site.
    /// </summary>
    internal static class CaseEndpoints
    {
        #region Public Methods

        public static IEndpointRouteBuilder MapCaseApi(this IEndpointRouteBuilder app)
        {
            var cases = app.MapGroup("/api/cases").RequireAuthorization(Policies.Viewer);

            cases.MapGet("", async (ClaimsPrincipal user, CaseService service, CancellationToken ct, string status = null) =>
                (await service.GetAsync(user, status, ct)).Select(c => ToDto(c.Case, c.Clips, null)));

            cases.MapGet("/{id}", async (string id, ClaimsPrincipal user, CaseService service, CameraDirectory directory, CancellationToken ct) =>
            {
                var view = await service.FindAsync(user, id, ct);
                return view == null ? Results.NotFound() : Results.Ok(ToDto(view.Case, view.ClipCount, view.Clips.Select(c => ToDto(c, directory)).ToList()));
            });

            cases.MapPost("", async (CaseRequest request, ClaimsPrincipal user, CaseService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(user, request, ct);
                return result.Status == ServiceStatus.Ok ? Results.Created($"/api/cases/{result.Value.Id}", ToDto(result.Value, 0, null)) : TenancyEndpoints.ToResult(result);
            })
                .RequireAuthorization(Policies.Operator)
                .Audit(AuditActions.CaseCreate, AuditTargets.Case);

            cases.MapPut("/{id}", async (string id, CaseRequest request, ClaimsPrincipal user, CaseService service, CancellationToken ct) =>
                TenancyEndpoints.ToResult(await service.UpdateAsync(user, id, request, ct), c => ToDto(c, null, null)))
                .RequireAuthorization(Policies.Operator)
                .Audit(AuditActions.CaseUpdate, AuditTargets.Case, details: context => new { status = context.GetArgument<CaseRequest>(1)?.Status });

            cases.MapDelete("/{id}", async (string id, ClaimsPrincipal user, CaseService service, CancellationToken ct) =>
            {
                var result = await service.DeleteAsync(user, id, ct);
                return result.Status == ServiceStatus.Ok ? Results.NoContent() : TenancyEndpoints.ToResult(result);
            })
                .RequireAuthorization(Policies.Admin)
                .Audit(AuditActions.CaseDelete, AuditTargets.Case);

            cases.MapPost("/{id}/clips", async (string id, ClipRequest request, ClaimsPrincipal user, CaseService service, CameraDirectory directory, CancellationToken ct) =>
            {
                var result = await service.AddClipAsync(user, id, request, ct);
                return result.Status == ServiceStatus.Ok ? Results.Created($"/api/cases/{id}/clips/{result.Value.Id}", ToDto(result.Value, directory)) : TenancyEndpoints.ToResult(result);
            })
                .RequireAuthorization(Policies.Operator)
                .Audit(AuditActions.CaseClipAdd, AuditTargets.Case, details: context =>
                {
                    var clip = context.GetArgument<ClipRequest>(1);
                    return new { cameras = clip?.CameraIds, from = clip?.From, to = clip?.To };
                });

            cases.MapPut("/{id}/clips/{clipId}", async (string id, string clipId, NoteRequest request, ClaimsPrincipal user, CaseService service, CameraDirectory directory, CancellationToken ct) =>
                TenancyEndpoints.ToResult(await service.UpdateNoteAsync(user, id, clipId, request?.Note, ct), c => ToDto(c, directory)))
                .RequireAuthorization(Policies.Operator)
                .Audit(AuditActions.CaseClipUpdate, AuditTargets.Case);

            cases.MapDelete("/{id}/clips/{clipId}", async (string id, string clipId, ClaimsPrincipal user, CaseService service, CancellationToken ct) =>
            {
                var result = await service.RemoveClipAsync(user, id, clipId, ct);
                return result.Status == ServiceStatus.Ok ? Results.NoContent() : TenancyEndpoints.ToResult(result);
            })
                .RequireAuthorization(Policies.Operator)
                .Audit(AuditActions.CaseClipRemove, AuditTargets.Case);

            return app;
        }

        #endregion

        #region Private Methods

        private static object ToDto(CaseRecord record, int? clipCount, object clips) => new
        {
            id = record.Id,
            name = record.Name,
            description = record.Description,
            status = record.Status,
            createdBy = record.CreatedByName,
            createdAt = record.CreatedAt,
            updatedAt = record.UpdatedAt,
            closedAt = record.ClosedAt,
            clipCount,
            clips
        };

        private static object ToDto(CaseClipRecord clip, CameraDirectory directory)
        {
            var cameras = CaseHolds.CameraIds(clip);
            return new
            {
                id = clip.Id,
                siteId = clip.SiteId,
                cameraIds = cameras,
                cameras = cameras.Select(id => new { id, name = directory.Find(id)?.Name ?? "Removed camera" }),
                from = DateTime.SpecifyKind(clip.From, DateTimeKind.Utc),
                to = DateTime.SpecifyKind(clip.To, DateTimeKind.Utc),
                note = clip.Note,
                eventId = clip.EventId,
                createdBy = clip.CreatedByName,
                createdAt = clip.CreatedAt
            };
        }

        #endregion
    }
}
