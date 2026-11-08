// Sarthak Killedar
// 2026-11-08

using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Configuration;

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
            var cameras = app.MapGroup("/api/config/cameras");

            cameras.MapGet("", async (CameraConfigurationService service, CancellationToken ct) => (await service.GetCamerasAsync(ct)).Select(ToDto));

            cameras.MapGet("/{id}", async (string id, CameraConfigurationService service, CancellationToken ct) =>
            {
                var camera = await service.GetCameraAsync(id, ct);
                return camera == null ? Results.NotFound() : Results.Ok(ToDto(camera));
            });

            cameras.MapPost("", async (CameraOptions camera, CameraConfigurationService service, CancellationToken ct) =>
                ToResult(await service.AddAsync(camera ?? new CameraOptions(), ct), created: true));

            cameras.MapPut("/{id}", async (string id, CameraOptions camera, CameraConfigurationService service, CancellationToken ct) =>
                ToResult(await service.UpdateAsync(id, camera ?? new CameraOptions(), ct), created: false));

            cameras.MapDelete("/{id}", async (string id, CameraConfigurationService service, CancellationToken ct) =>
            {
                var result = await service.RemoveAsync(id, ct);
                return result.Status == ConfigurationStatus.Ok ? Results.NoContent() : ToResult(result, created: false);
            });

            return app;
        }

        #endregion

        #region Private Methods

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
            element["hasPassword"] = !string.IsNullOrEmpty(camera.Password);
            return JsonSerializer.SerializeToElement(element, Json);
        }

        #endregion
    }
}
