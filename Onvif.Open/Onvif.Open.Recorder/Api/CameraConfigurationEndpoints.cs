// Sarthak Killedar
// 2026-11-08

using System;
using System.Security.Claims;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Configuration;

using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: the cameras' configuration, changed while the recorder runs. Passwords are write-only:
    /// responses carry "hasPassword" instead.
    /// </summary>
    internal static class CameraConfigurationEndpoints
    {
        #region Private Variables

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapCameraConfigurationApi(this IEndpointRouteBuilder app)
        {
            // Administrators configure the cameras of the sites they see (the access middleware hides the others' ids).
            var cameras = app.MapGroup("/api/config/cameras").RequireAuthorization(Policies.Admin);

            cameras.MapGet("", async (ClaimsPrincipal user, CameraConfigurationService service, CancellationToken ct) =>
                (await service.GetCamerasAsync(user.OrganizationId(), ct)).Where(c => user.CanSeeSite(c.OrganizationId, c.SiteId)).Select(ToDto));

            cameras.MapGet("/{id}", async (string id, ClaimsPrincipal user, CameraConfigurationService service, CancellationToken ct) =>
            {
                var camera = await service.GetCameraAsync(user.OrganizationId(), id, ct);
                return camera == null ? Results.NotFound() : Results.Ok(ToDto(camera));
            });

            cameras.MapPost("", async (CameraOptions camera, ClaimsPrincipal user, CameraConfigurationService service, CancellationToken ct) =>
            {
                camera ??= new CameraOptions();
                return !user.CanSeeSite(user.OrganizationId(), camera.SiteId)
                    ? SiteNotAllowed()
                    : ToResult(await service.AddAsync(user.OrganizationId(), camera, ct), created: true);
            })
                .Audit(AuditActions.CameraCreate, AuditTargets.Camera);

            cameras.MapPut("/{id}", async (string id, CameraOptions camera, ClaimsPrincipal user, CameraConfigurationService service, CancellationToken ct) =>
            {
                camera ??= new CameraOptions();
                return !user.CanSeeSite(user.OrganizationId(), camera.SiteId)
                    ? SiteNotAllowed()
                    : ToResult(await service.UpdateAsync(user.OrganizationId(), id, camera, ct), created: false);
            })
                .Audit(AuditActions.CameraUpdate, AuditTargets.Camera);

            // Tests settings before they are saved; with "id" of a saved camera, its stored password is used when none is given.
            cameras.MapPost("/test", async (CameraOptions camera, ClaimsPrincipal user, CameraDirectory directory, CameraConfigurationService service, CancellationToken ct) =>
            {
                camera = camera ?? new CameraOptions();

                if (string.IsNullOrWhiteSpace(camera.OnvifUrl) == string.IsNullOrWhiteSpace(camera.RtspUrl))
                {
                    return Results.BadRequest(new { errors = new[] { "Give exactly one of onvifUrl or rtspUrl." } });
                }

                if (camera.Password == null && !string.IsNullOrWhiteSpace(camera.Id) && user.CanSeeCamera(directory, camera.Id))
                {
                    camera.Password = (await service.GetCameraAsync(user.OrganizationId(), camera.Id, ct))?.Password;
                }

                return Results.Ok(await CameraProbe.ProbeAsync(camera, ct));
            });

            cameras.MapDelete("/{id}", async (string id, ClaimsPrincipal user, CameraConfigurationService service, CancellationToken ct) =>
            {
                var result = await service.RemoveAsync(user.OrganizationId(), id, ct);
                return result.Status == ConfigurationStatus.Ok ? Results.NoContent() : ToResult(result, created: false);
            })
                .Audit(AuditActions.CameraDelete, AuditTargets.Camera);

            return app;
        }

        #endregion

        #region Private Methods

        private static IResult SiteNotAllowed() => Results.BadRequest(new { errors = new[] { "Choose one of your sites for the camera." } });

        private static IResult ToResult(ConfigurationResult result, bool created)
        {
            switch (result.Status)
            {
                case ConfigurationStatus.Ok:
                    return created
                        ? Results.Created($"/api/config/cameras/{Uri.EscapeDataString(result.Camera.Id)}", ToDto(result.Camera))
                        : Results.Ok(ToDto(result.Camera));
                case ConfigurationStatus.NotFound:
                    return Results.NotFound();
                case ConfigurationStatus.Conflict:
                    return Results.Conflict(new { errors = result.Errors });
                default:
                    return Results.BadRequest(new { errors = result.Errors });
            }
        }

        /// <summary>The configuration as JSON, without the password.</summary>
        private static JsonElement ToDto(CameraOptions camera)
        {
            var element = JsonSerializer.SerializeToNode(camera, Json).AsObject();
            element.Remove("password");
            element.Remove("organizationId");
            element["hasPassword"] = !string.IsNullOrEmpty(camera.Password);
            return JsonSerializer.SerializeToElement(element, Json);
        }

        #endregion
    }
}
