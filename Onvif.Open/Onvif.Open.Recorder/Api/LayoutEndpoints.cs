// Sarthak Killedar
// 2026-11-08

using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Layouts;
using Onvif.Open.Recorder.Security;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: live view layouts. Every signed-in user has their own; shared layouts are managed by administrators.
    /// </summary>
    internal static class LayoutEndpoints
    {
        #region Public Methods

        public static IEndpointRouteBuilder MapLayoutApi(this IEndpointRouteBuilder app)
        {
            var layouts = app.MapGroup("/api/layouts").RequireAuthorization(Policies.Viewer);

            layouts.MapGet("", async (ClaimsPrincipal principal, LayoutService service, CancellationToken ct) =>
                await service.GetAsync(User(principal), ct));

            layouts.MapPost("", async (LayoutRequest request, ClaimsPrincipal principal, LayoutService service, CancellationToken ct) =>
            {
                var result = await service.CreateAsync(User(principal), request, ct);
                return result.Status == LayoutStatus.Ok
                    ? Results.Created($"/api/layouts/{result.Layout.Id}", result.Layout)
                    : ToResult(result);
            });

            layouts.MapPut("/{id}", async (string id, LayoutRequest request, ClaimsPrincipal principal, LayoutService service, CancellationToken ct) =>
                ToResult(await service.UpdateAsync(User(principal), id, request, ct)));

            layouts.MapDelete("/{id}", async (string id, ClaimsPrincipal principal, LayoutService service, CancellationToken ct) =>
            {
                var result = await service.DeleteAsync(User(principal), id, ct);
                return result.Status == LayoutStatus.Ok ? Results.NoContent() : ToResult(result);
            });

            return app;
        }

        #endregion

        #region Private Methods

        private static LayoutUser User(ClaimsPrincipal principal)
        {
            return new LayoutUser(principal.FindFirstValue(ClaimTypes.NameIdentifier), principal.IsInRole(Roles.Admin));
        }

        private static IResult ToResult(LayoutResult result)
        {
            switch (result.Status)
            {
                case LayoutStatus.Ok:
                    return Results.Ok(result.Layout);
                case LayoutStatus.NotFound:
                    return Results.NotFound();
                case LayoutStatus.Forbidden:
                    return Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status403Forbidden);
                default:
                    return Results.BadRequest(new { errors = result.Errors });
            }
        }

        #endregion
    }
}
