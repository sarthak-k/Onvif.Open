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

        /// <summary>Longest clip one download may cover.</summary>
        private static readonly TimeSpan MaxClipDuration = TimeSpan.FromHours(6);

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapPlaybackApi(this IEndpointRouteBuilder app)
        {
            var camera = app.MapGroup("/api/cameras/{id}");

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
                return Results.Text(stream.GetPlaylist(sequence => $"live/{sequence.ToString(CultureInfo.InvariantCulture)}.ts"), PlaylistType);
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

                var text = await playlist.GetPlaylistAsync(cameraId, start, end, s => $"segments/{Uri.EscapeDataString(s.FileName)}", ct);
                NoCache(http);
                return Results.Text(text, PlaylistType);
            });

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
            });

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
