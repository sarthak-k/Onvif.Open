// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Imaging;
using Onvif.Open.Recorder.Configuration;

using Onvif.Open.Recorder.Security;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: image settings and focus of ONVIF cameras.
    /// </summary>
    internal static class ImagingEndpoints
    {
        #region Public Variables

        public sealed class FocusRequest
        {
            /// <summary>Continuous move speed -1 (near) .. 1 (far).</summary>
            public double? Speed { get; set; }

            /// <summary>Stop again after this many milliseconds (max 30000).</summary>
            public int? DurationMs { get; set; }

            /// <summary>Absolute focus position (device range).</summary>
            public double? Position { get; set; }
        }

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapImagingApi(this IEndpointRouteBuilder app)
        {
            var imaging = app.MapGroup("/api/cameras/{id}/imaging").RequireAuthorization(Policies.Viewer);

            imaging.MapGet("", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async i => Results.Ok(await i.GetSettingsAsync(ct))));

            // Only the properties present in the body are changed, e.g. { "irCutFilter": "Auto", "brightness": 60 }.
            imaging.MapPatch("", (string id, ImagingSettings changes, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async i =>
                {
                    await i.UpdateAsync(changes ?? new ImagingSettings(), ct);
                    return Results.Ok(await i.GetSettingsAsync(ct));
                })).RequireAuthorization(Policies.Admin);

            imaging.MapGet("/options", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async i => Results.Ok(await i.GetOptionsAsync(ct))));

            imaging.MapGet("/focus", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async i => Results.Ok(await i.GetFocusStatusAsync(ct))));

            imaging.MapPost("/focus", (string id, FocusRequest request, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async i =>
                {
                    if (request?.Position is double position)
                    {
                        await i.FocusToAsync(position, ct);
                    }
                    else if (request?.Speed is double speed)
                    {
                        var duration = request.DurationMs is int ms ? TimeSpan.FromMilliseconds(Math.Clamp(ms, 1, 30000)) : (TimeSpan?)null;
                        await i.MoveFocusAsync(speed, duration, ct);
                    }
                    else
                    {
                        return Results.BadRequest(new { error = "Give either a position or a speed." });
                    }

                    return Results.NoContent();
                })).RequireAuthorization(Policies.Operator);

            imaging.MapPost("/focus/stop", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async i =>
                {
                    await i.StopFocusAsync(ct);
                    return Results.NoContent();
                })).RequireAuthorization(Policies.Operator);

            return app;
        }

        #endregion

        #region Private Methods

        private static async Task<IResult> Run(string id, IRecordingManager manager, ConfiguredRecording recording, Func<ICameraImaging, Task<IResult>> action)
        {
            var cameraId = manager.GetStatus().FirstOrDefault(s => string.Equals(s.CameraId, id, StringComparison.OrdinalIgnoreCase))?.CameraId;
            if (cameraId == null)
            {
                return Results.NotFound();
            }

            try
            {
                return await action(recording.GetImaging(cameraId));
            }
            catch (NotSupportedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status501NotImplemented);
            }
            catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        }

        #endregion
    }
}
