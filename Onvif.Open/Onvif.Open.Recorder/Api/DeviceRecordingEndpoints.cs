// Sarthak Killedar
// 2026-11-07

using System;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Abstract.Interface.Replay;
using Onvif.Open.Core.Replay;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recording.Implementation.Export;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: recordings stored on the cameras themselves (ONVIF Profile G): list them and export clips.
    /// </summary>
    internal static class DeviceRecordingEndpoints
    {
        #region Private Variables

        /// <summary>Longest clip one export may cover.</summary>
        private static readonly TimeSpan MaxExportDuration = TimeSpan.FromHours(6);

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapDeviceRecordingsApi(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/cameras/{id}/device-recordings");

            group.MapGet("", (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async r =>
                {
                    var summary = await r.GetSummaryAsync(ct);
                    var recordings = await r.GetRecordingsAsync(ct);

                    return Results.Ok(new
                    {
                        summary,
                        recordings = recordings.Select(x => new
                        {
                            x.Token,
                            x.SourceName,
                            x.Location,
                            x.Content,
                            x.Earliest,
                            x.Latest,
                            x.Status,
                            x.Tracks
                        })
                    });
                }));

            // Streams the clip as it is replayed (as fast as the camera can send it), so large clips need no buffering.
            group.MapGet("/{token}/export", (string id, string token, DateTimeOffset? from, DateTimeOffset? to, HttpContext http, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
                Run(id, manager, recording, async r =>
                {
                    if (!from.HasValue || !to.HasValue || to <= from || to - from > MaxExportDuration)
                    {
                        return Results.BadRequest(new { error = $"'from' and 'to' are required, 'to' after 'from', at most {MaxExportDuration.TotalHours} hours apart." });
                    }

                    var replay = await r.StartReplayAsync(token, from.Value, new ReplayOptions { To = to, RealTime = false }, ct);

                    await using (replay)
                    {
                        var fileName = string.Format(CultureInfo.InvariantCulture, "{0}_{1:yyyyMMdd'T'HHmmss'Z'}{2}", id, from.Value.UtcDateTime, VideoExport.GetFileExtension(replay));

                        http.Response.ContentType = "video/mp2t";
                        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

                        try
                        {
                            await VideoExport.WriteAsync(replay, http.Response.Body, cancellationToken: ct);
                        }
                        catch (Exception) when (http.Response.HasStarted)
                        {
                            // Too late for an error status: abort, so the client does not mistake a partial clip for a complete one.
                            http.Abort();
                        }
                    }

                    return Results.Empty;
                }));

            return app;
        }

        #endregion

        #region Private Methods

        private static async Task<IResult> Run(string id, IRecordingManager manager, ConfiguredRecording recording, Func<ICameraRecordings, Task<IResult>> action)
        {
            var cameraId = manager.GetStatus().FirstOrDefault(s => string.Equals(s.CameraId, id, StringComparison.OrdinalIgnoreCase))?.CameraId;
            if (cameraId == null)
            {
                return Results.NotFound();
            }

            try
            {
                return await action(recording.GetDeviceRecordings(cameraId));
            }
            catch (NotSupportedException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status501NotImplemented);
            }
            catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException || ex is System.IO.IOException || ex is InvalidOperationException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        }

        #endregion
    }
}
