// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Ptz;
using Onvif.Open.Recorder.Configuration;

using Onvif.Open.Recorder.Security;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: pan/tilt/zoom of ONVIF cameras.
    /// </summary>
    internal static class PtzEndpoints
    {
        #region Public Variables

        /// <summary>Body of move and position requests (generic spaces: speeds -1..1, positions pan/tilt -1..1 and zoom 0..1).</summary>
        public sealed class PtzRequest
        {
            public double? Pan { get; set; }
            public double? Tilt { get; set; }
            public double? Zoom { get; set; }

            /// <summary>Move only: stop again after this many milliseconds (max 60000); keep moving until /stop when omitted.</summary>
            public int? DurationMs { get; set; }
        }

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapPtzApi(this IEndpointRouteBuilder app)
        {
            var ptz = app.MapGroup("/api/cameras/{id}/ptz").RequireAuthorization(Policies.Viewer);

            ptz.MapGet("", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p => Results.Ok(ToDto(await p.GetStatusAsync(ct)))));

            ptz.MapPost("/move", (string id, PtzRequest request, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p =>
                {
                    var duration = request?.DurationMs is int ms ? TimeSpan.FromMilliseconds(Math.Clamp(ms, 1, 60000)) : (TimeSpan?)null;
                    await p.MoveAsync(request?.Pan ?? 0, request?.Tilt ?? 0, request?.Zoom ?? 0, duration, ct);
                    return Results.NoContent();
                })).RequireAuthorization(Policies.Operator);

            ptz.MapPost("/stop", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p =>
                {
                    await p.StopAsync(ct);
                    return Results.NoContent();
                })).RequireAuthorization(Policies.Operator);

            ptz.MapPost("/position", (string id, PtzRequest request, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p =>
                {
                    await p.MoveToAsync(new PtzVector(request?.Pan, request?.Tilt, request?.Zoom), ct);
                    return Results.NoContent();
                })).RequireAuthorization(Policies.Operator);

            ptz.MapGet("/presets", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p => Results.Ok((await p.GetPresetsAsync(ct)).Select(preset => new
                {
                    preset.Token,
                    preset.Name,
                    position = ToDto(preset.Position)
                }))));

            ptz.MapPost("/presets/{preset}/goto", (string id, string preset, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p =>
                {
                    await p.GoToPresetAsync(preset, ct);
                    return Results.NoContent();
                })).RequireAuthorization(Policies.Operator);

            ptz.MapPut("/presets/{preset}", (string id, string preset, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p => Results.Ok(new { token = await p.SavePresetAsync(preset, ct), name = preset }))).RequireAuthorization(Policies.Operator);

            ptz.MapDelete("/presets/{preset}", (string id, string preset, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p => await p.RemovePresetAsync(preset, ct) ? Results.NoContent() : Results.NotFound())).RequireAuthorization(Policies.Operator);

            ptz.MapPost("/home", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p =>
                {
                    await p.GoHomeAsync(ct);
                    return Results.NoContent();
                })).RequireAuthorization(Policies.Operator);

            ptz.MapPut("/home", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async p =>
                {
                    await p.SetHomeAsync(ct);
                    return Results.NoContent();
                })).RequireAuthorization(Policies.Operator);

            return app;
        }

        #endregion

        #region Private Methods

        private static async Task<IResult> Run(string id, IRecordingManager manager, ConfiguredRecording recording, Func<ICameraPtz, Task<IResult>> action)
        {
            var cameraId = manager.GetStatus().FirstOrDefault(s => string.Equals(s.CameraId, id, StringComparison.OrdinalIgnoreCase))?.CameraId;
            if (cameraId == null)
            {
                return Results.NotFound();
            }

            try
            {
                return await action(recording.GetPtz(cameraId));
            }
            catch (NotSupportedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status501NotImplemented);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
            }
            catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        }

        private static object ToDto(PtzStatus status)
        {
            return new
            {
                position = ToDto(status.Position),
                panTilt = status.PanTilt.ToString(),
                zoom = status.Zoom.ToString(),
                status.IsMoving,
                status.Error,
                status.UtcTime
            };
        }

        private static object ToDto(PtzVector vector)
        {
            return vector == null ? null : new { vector.Pan, vector.Tilt, vector.Zoom };
        }

        #endregion
    }
}
