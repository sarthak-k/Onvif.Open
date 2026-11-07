// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: plugin status, plugin events (linked to the recording that covers them) and camera snapshots.
    /// </summary>
    internal static class PluginEndpoints
    {
        #region Public Methods

        public static IEndpointRouteBuilder MapPluginApi(this IEndpointRouteBuilder app)
        {
            var api = app.MapGroup("/api");

            api.MapGet("/plugins", (IPluginHost host) => host.GetStatus());

            api.MapGet("/events", (IPluginHost host, IRecordingManager manager, string cameraId, string type, string source, DateTimeOffset? since, PluginEventSeverity? severity, int? limit) =>
            {
                var events = host.GetEvents(new PluginEventQuery
                {
                    CameraId = cameraId,
                    Type = type,
                    Source = source,
                    Since = since,
                    MinimumSeverity = severity,
                    Limit = Math.Clamp(limit ?? 100, 1, 1000)
                });

                return events.Select(e => new
                {
                    e.Id,
                    e.Source,
                    e.Type,
                    e.CameraId,
                    severity = e.Severity.ToString(),
                    e.Message,
                    e.Timestamp,
                    e.Data,
                    recording = FindRecording(manager, e)
                });
            });

            api.MapGet("/cameras/{id}/snapshot", async (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
            {
                if (!manager.GetStatus().Any(s => string.Equals(s.CameraId, id, StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.NotFound();
                }

                try
                {
                    return Results.File(await recording.GetSnapshotAsync(id, ct), "image/jpeg");
                }
                catch (NotSupportedException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status501NotImplemented);
                }
                catch (HttpRequestException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            });

            return app;
        }

        #endregion

        #region Private Methods

        /// <summary>The segment recorded at the time of the event: the video evidence for it.</summary>
        private static object FindRecording(IRecordingManager manager, PluginEvent pluginEvent)
        {
            if (pluginEvent.CameraId == null)
            {
                return null;
            }

            var segment = manager.GetSegments(pluginEvent.CameraId)
                .LastOrDefault(s => s.StartTime <= pluginEvent.Timestamp && (s.IsActive || s.LastWriteTime >= pluginEvent.Timestamp));

            return segment == null
                ? null
                : new
                {
                    segment.FileName,
                    offset = pluginEvent.Timestamp - segment.StartTime,
                    url = $"/api/cameras/{Uri.EscapeDataString(segment.CameraId)}/segments/{Uri.EscapeDataString(segment.FileName)}"
                };
        }

        #endregion
    }
}
