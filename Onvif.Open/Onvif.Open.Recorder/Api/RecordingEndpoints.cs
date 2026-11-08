// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Recording;

using Onvif.Open.Recorder.Security;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: camera status and control, segment listing / download / deletion, storage and retention.
    /// </summary>
    internal static class RecordingEndpoints
    {
        #region Public Methods

        public static IEndpointRouteBuilder MapRecordingApi(this IEndpointRouteBuilder app)
        {
            var api = app.MapGroup("/api").RequireAuthorization(Policies.Viewer);

            api.MapGet("/cameras", (IRecordingManager manager) => manager.GetStatus().Select(ToDto));

            api.MapGet("/cameras/{id}", (string id, IRecordingManager manager) =>
            {
                var status = Find(manager, id);
                return status == null ? Results.NotFound() : Results.Ok(ToDto(status));
            });

            api.MapPost("/cameras/{id}/start", async (string id, IRecordingManager manager, CancellationToken ct) =>
            {
                if (Find(manager, id) == null)
                {
                    return Results.NotFound();
                }

                await manager.StartCameraAsync(id, ct);
                return Results.Accepted($"/api/cameras/{id}");
            }).RequireAuthorization(Policies.Operator);

            api.MapPost("/cameras/{id}/stop", async (string id, IRecordingManager manager, CancellationToken ct) =>
            {
                if (Find(manager, id) == null)
                {
                    return Results.NotFound();
                }

                await manager.StopCameraAsync(id, ct);
                return Results.Ok(ToDto(Find(manager, id)));
            }).RequireAuthorization(Policies.Operator);

            api.MapPost("/cameras/{id}/trigger", (string id, IRecordingManager manager, double? seconds) =>
            {
                var status = Find(manager, id);
                if (status == null)
                {
                    return Results.NotFound();
                }

                if (status.Mode != RecordingMode.Triggered)
                {
                    return Results.Conflict(new { error = "The camera records continuously; triggers only apply to triggered mode." });
                }

                manager.TriggerRecording(status.CameraId, TimeSpan.FromSeconds(Math.Clamp(seconds ?? 30, 0, 3600)));
                return Results.Accepted($"/api/cameras/{status.CameraId}", ToDto(Find(manager, id)));
            }).RequireAuthorization(Policies.Operator);

            api.MapGet("/camera-events", (Onvif.Open.Recorder.Hosting.CameraEventStore store, string cameraId, string topic, int? limit) =>
                store.Query(cameraId, topic, Math.Clamp(limit ?? 100, 1, 1000)).Select(e => new
                {
                    e.CameraId,
                    e.Topic,
                    e.TopicPath,
                    e.UtcTime,
                    e.PropertyOperation,
                    state = e.GetState(),
                    e.Source,
                    e.Data
                }));

            api.MapGet("/cameras/{id}/segments", (string id, IRecordingManager manager, DateTimeOffset? from, DateTimeOffset? to) =>
                manager.GetSegments(id)
                    .Where(s => (!from.HasValue || s.LastWriteTime >= from) && (!to.HasValue || s.StartTime <= to))
                    .Select(s => ToDto(s, id)));

            // The file is looked up in the storage listing by name, so no user supplied path ever reaches the file system.
            api.MapGet("/cameras/{id}/segments/{fileName}", async (string id, string fileName, IRecordingManager manager, CancellationToken ct) =>
            {
                var segment = FindSegment(manager, id, fileName);

                if (segment == null)
                {
                    return Results.NotFound();
                }

                if (segment.IsLocal && System.IO.File.Exists(segment.Path))
                {
                    return Results.File(segment.Path, ContentType(segment.FileName), segment.FileName, enableRangeProcessing: true);
                }

                // Only in the archive: streamed from the store (ranges when the store's stream can seek).
                var stream = await manager.OpenSegmentAsync(segment, ct);
                return stream == null
                    ? Results.NotFound()
                    : Results.Stream(stream, ContentType(segment.FileName), segment.FileName, enableRangeProcessing: stream.CanSeek);
            });

            api.MapDelete("/cameras/{id}/segments/{fileName}", async (string id, string fileName, IRecordingManager manager, CancellationToken ct) =>
            {
                var segment = FindSegment(manager, id, fileName);

                if (segment == null)
                {
                    return Results.NotFound();
                }

                if (!await manager.DeleteSegmentAsync(segment, ct))
                {
                    return Results.Conflict(new { error = "The segment is being recorded or could not be deleted." });
                }

                return Results.NoContent();
            }).RequireAuthorization(Policies.Admin);

            api.MapGet("/storage", (IRecordingManager manager) =>
            {
                var segments = manager.GetSegments();
                var storage = manager.GetStorageInformation();

                return new
                {
                    location = storage.Location,
                    availableFreeBytes = storage.AvailableFreeBytes,
                    archive = storage.Archive == null ? null : new
                    {
                        provider = storage.Archive.Provider,
                        location = storage.Archive.Location,
                        isAvailable = storage.Archive.IsAvailable,
                        pendingUploads = storage.Archive.PendingUploads,
                        lastUploadAt = storage.Archive.LastUploadAt,
                        lastError = storage.Archive.LastError,
                        lastErrorAt = storage.Archive.LastErrorAt
                    },
                    localBytes = segments.Where(s => s.IsLocal).Sum(s => s.Size),
                    archivedBytes = segments.Where(s => s.IsArchived).Sum(s => s.Size),
                    totalBytes = segments.Sum(s => s.Size),
                    segmentCount = segments.Count,
                    cameras = segments.GroupBy(s => s.CameraId).Select(g => new
                    {
                        cameraId = g.Key,
                        segmentCount = g.Count(),
                        totalBytes = g.Sum(s => s.Size),
                        oldest = g.Min(s => s.StartTime),
                        newest = g.Max(s => s.LastWriteTime)
                    })
                };
            });

            api.MapPost("/retention", async (IRecordingManager manager, CancellationToken ct) =>
            {
                var deleted = await manager.ApplyRetentionAsync(ct);
                return new { deleted = deleted.Select(s => ToDto(s, s.CameraId)) };
            }).RequireAuthorization(Policies.Admin);

            app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

            return app;
        }

        #endregion

        #region Private Methods

        private static CameraRecordingStatus Find(IRecordingManager manager, string id)
        {
            return manager.GetStatus().FirstOrDefault(s => string.Equals(s.CameraId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static RecordingSegment FindSegment(IRecordingManager manager, string id, string fileName)
        {
            return manager.GetSegments(id).FirstOrDefault(s => string.Equals(s.FileName, fileName, StringComparison.Ordinal));
        }

        private static object ToDto(CameraRecordingStatus status)
        {
            return new
            {
                cameraId = status.CameraId,
                state = status.State.ToString(),
                mode = status.Mode.ToString(),
                status.IsTriggered,
                status.LastTriggeredAt,
                codec = status.Codec.ToString(),
                audioCodec = status.AudioCodec == Core.Streaming.AudioCodec.Unknown ? null : status.AudioCodec.ToString(),
                status.RecordsAudio,
                transport = status.Transport?.ToString(),
                streamUri = Redact(status.StreamUri),
                currentSegment = status.CurrentSegment == null ? null : System.IO.Path.GetFileName(status.CurrentSegment),
                status.FramesRecorded,
                status.BytesRecorded,
                status.SegmentsCompleted,
                status.LastFrameAt,
                status.LastError,
                status.LastErrorAt
            };
        }

        private static object ToDto(RecordingSegment segment, string cameraId)
        {
            return new
            {
                cameraId = segment.CameraId,
                fileName = segment.FileName,
                segment.StartTime,
                segment.LastWriteTime,
                segment.Size,
                segment.IsActive,
                segment.IsLocal,
                segment.IsArchived,
                url = $"/api/cameras/{Uri.EscapeDataString(segment.CameraId)}/segments/{Uri.EscapeDataString(segment.FileName)}"
            };
        }

        /// <summary>Never expose credentials embedded in a stream URI.</summary>
        internal static string Redact(Uri uri)
        {
            if (uri == null)
            {
                return null;
            }

            return string.IsNullOrEmpty(uri.UserInfo) ? uri.ToString() : new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.ToString();
        }

        private static string ContentType(string fileName)
        {
            return fileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ? "video/mp2t" : "application/octet-stream";
        }

        #endregion
    }
}
