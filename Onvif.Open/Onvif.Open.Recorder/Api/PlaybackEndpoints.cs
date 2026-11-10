// Sarthak Killedar
// 2026-11-07

using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Recorder.Hosting;
using Onvif.Open.Recording.Implementation.Export;
using Onvif.Open.Recording.Implementation.Hls;

using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API for browsers: live HLS, HLS playback of recordings and clip download.
    /// </summary>
    internal static class PlaybackEndpoints
    {
        #region Private Variables

        private const string PlaylistType = "application/vnd.apple.mpegurl";
        private const string SegmentType = "video/mp2t";
        private const string Mp4Type = "video/mp4";

        /// <summary>The RFC 6381 codec of a low-latency stream ("avc1.64001f").</summary>
        public const string CodecHeader = "X-Video-Codec";

        /// <summary>Longest clip one download may cover.</summary>
        private static readonly TimeSpan MaxClipDuration = TimeSpan.FromHours(6);

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapPlaybackApi(this IEndpointRouteBuilder app)
        {
            var camera = app.MapGroup("/api/cameras/{id}").RequireAuthorization(Policies.Viewer);

            camera.MapGet("/live.m3u8", async (string id, LiveViewService live, HttpContext http, CancellationToken ct) =>
            {
                var stream = live.Watch(id);
                if (stream == null)
                {
                    return Results.NotFound();
                }

                // A player needs a couple of segments to start.
                if (!await stream.WaitForSegmentsAsync(2, live.Options.StartTimeout, ct))
                {
                    return Results.Problem("No video from the camera yet.", statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                NoCache(http);
                return Results.Text(stream.GetPlaylist(sequence => SecurityConfiguration.WithMediaToken(http, $"live/{sequence.ToString(CultureInfo.InvariantCulture)}.ts")), PlaylistType);
            })
                .Audit(AuditActions.VideoLive, AuditTargets.Camera, views: true);

            // Low latency: one long response, the init segment then a fragment per frame (H.264; 415 for other codecs:
            // play the HLS stream instead). The codec goes in a header so the player can set up before the first bytes.
            camera.MapGet("/live.mp4", async (string id, LiveViewService live, AuditLog audit, CameraDirectory directory, HttpContext http, CancellationToken ct) =>
            {
                var stream = live.WatchMp4(id);
                if (stream == null)
                {
                    return Results.NotFound();
                }

                if (!await stream.WaitUntilReadyAsync(live.Options.StartTimeout, ct))
                {
                    return stream.IsSupported
                        ? Results.Problem("No video from the camera yet.", statusCode: StatusCodes.Status503ServiceUnavailable)
                        : Results.Problem("This camera's video needs the HLS player.", statusCode: StatusCodes.Status415UnsupportedMediaType);
                }

                using (var viewer = stream.Watch())
                {
                    NoCache(http);
                    http.Response.ContentType = Mp4Type;
                    http.Response.Headers[CodecHeader] = stream.CodecString;
                    http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
                    await http.Response.StartAsync(ct);

                    // Recorded when watching starts (the response lasts as long as the viewer stays).
                    if (audit.IsNewView(http.User, AuditActions.VideoLive, id))
                    {
                        audit.Record(http, AuditActions.VideoLive, AuditTargets.Camera, id, directory.Find(id)?.Name, null);
                    }

                    try
                    {
                        await foreach (var fragment in viewer.Fragments.ReadAllAsync(ct))
                        {
                            await http.Response.Body.WriteAsync(fragment, ct);
                            await http.Response.Body.FlushAsync(ct);
                        }
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        // The viewer left.
                    }
                }

                return Results.Empty;
            });

            camera.MapGet("/live/{sequence:long}.ts", (string id, long sequence, LiveViewService live) =>
            {
                var stream = live.Watch(id);
                return stream != null && stream.TryGetSegment(sequence, out var data)
                    ? Results.Bytes(data, SegmentType)
                    : Results.NotFound();
            });

            camera.MapGet("/recordings.m3u8", async (string id, DateTimeOffset? from, DateTimeOffset? to, IRecordingManager manager, RecordingPlaylist playlist, HttpContext http, CancellationToken ct) =>
            {
                var cameraId = Find(manager, id);
                if (cameraId == null)
                {
                    return Results.NotFound();
                }

                var end = to ?? DateTimeOffset.UtcNow;
                var start = from ?? end.AddHours(-1);

                if (end <= start)
                {
                    return Results.BadRequest(new { error = "'to' must be after 'from'." });
                }

                var text = await playlist.GetPlaylistAsync(cameraId, start, end, s => SecurityConfiguration.WithMediaToken(http, $"segments/{Uri.EscapeDataString(s.FileName)}"), ct);
                NoCache(http);
                return Results.Text(text, PlaylistType);
            })
                .Audit(AuditActions.VideoPlayback, AuditTargets.Camera, views: true);

            camera.MapGet("/clip", async (string id, DateTimeOffset? from, DateTimeOffset? to, IRecordingManager manager, HttpContext http, CancellationToken ct) =>
            {
                var cameraId = Find(manager, id);
                if (cameraId == null)
                {
                    return Results.NotFound();
                }

                if (!from.HasValue || !to.HasValue || to <= from || to - from > MaxClipDuration)
                {
                    return Results.BadRequest(new { error = $"'from' and 'to' are required, 'to' after 'from', at most {MaxClipDuration.TotalHours} hours apart." });
                }

                if (!manager.GetSegments(cameraId).Any(s => s.StartTime <= to && s.LastWriteTime >= from))
                {
                    return Results.NotFound(new { error = "Nothing was recorded in that time." });
                }

                http.Response.ContentType = SegmentType;
                http.Response.Headers.ContentDisposition = string.Format(CultureInfo.InvariantCulture, "attachment; filename=\"{0}_{1:yyyyMMdd'T'HHmmss'Z'}.ts\"", cameraId, from.Value.UtcDateTime);

                try
                {
                    await ClipExport.WriteAsync(manager, cameraId, from.Value, to.Value, http.Response.Body, ct);
                }
                catch (Exception) when (http.Response.HasStarted)
                {
                    // Too late for an error status: abort, so a partial clip is not mistaken for a complete one.
                    http.Abort();
                }

                return Results.Empty;
            })
                .Audit(AuditActions.VideoExport, AuditTargets.Camera, details: c => new { from = c.HttpContext.Request.Query["from"].ToString(), to = c.HttpContext.Request.Query["to"].ToString() });

            return app;
        }

        #endregion

        #region Private Methods

        private static string Find(IRecordingManager manager, string id)
        {
            return manager.GetStatus().FirstOrDefault(s => string.Equals(s.CameraId, id, StringComparison.OrdinalIgnoreCase))?.CameraId;
        }

        private static void NoCache(HttpContext http)
        {
            http.Response.Headers.CacheControl = "no-cache";
        }

        #endregion
    }
}
