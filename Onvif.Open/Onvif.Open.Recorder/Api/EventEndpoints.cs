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
using Onvif.Open.Recorder.Events;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: stored events (motion, people, vehicles, tampering, inputs, offline cameras, plugin findings) of the
    /// sites the user may see, newest first.
    /// </summary>
    internal static class EventEndpoints
    {
        public static IEndpointRouteBuilder MapEventApi(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/events", async (ClaimsPrincipal user, IEventStore store, CameraDirectory directory, string cameraId, string siteId, string category,
                DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset? before, int? limit, CancellationToken ct) =>
            {
                if (cameraId != null && !user.CanSeeCamera(directory, cameraId))
                {
                    return Results.NotFound();
                }

                if (category != null && !EventCategories.All.Contains(category))
                {
                    return Results.BadRequest(new { errors = new[] { $"Category must be one of {string.Join(", ", EventCategories.All)}." } });
                }

                var events = await store.QueryAsync(new EventQuery
                {
                    OrganizationId = user.OrganizationId(),
                    Sites = user.Sites(),
                    CameraId = cameraId?.ToLowerInvariant(),
                    SiteId = siteId,
                    Category = category,
                    From = from?.UtcDateTime,
                    To = to?.UtcDateTime,
                    Before = before?.UtcDateTime,
                    Limit = Math.Clamp(limit ?? 100, 1, 1000)
                }, ct);

                return Results.Ok(events.Select(e => ToDto(e, directory)));
            }).RequireAuthorization(Policies.Viewer);

            return app;
        }

        private static object ToDto(EventRecord e, CameraDirectory directory) => new
        {
            id = e.Id,
            cameraId = e.CameraId,
            cameraName = e.CameraId == null ? null : directory.Find(e.CameraId)?.Name,
            siteId = e.SiteId,
            category = e.Category,
            severity = e.Severity,
            source = e.Source,
            title = e.Title,
            topic = e.Topic,
            startTime = Utc(e.StartTime),
            endTime = e.EndTime.HasValue ? Utc(e.EndTime.Value) : (DateTimeOffset?)null,
            data = e.Data == null ? (JsonElement?)null : JsonDocument.Parse(e.Data).RootElement
        };

        private static DateTimeOffset Utc(DateTime time) => new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Utc));
    }
}
