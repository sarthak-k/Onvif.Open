// Sarthak Killedar
// 2026-11-09

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Exports;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// MP4 exports with a watermark, made from real recordings with ffmpeg, and share links to them.
    /// </summary>
    public sealed class ExportsApiTest : IDisposable
    {
        public ExportsApiTest()
        {
            _storage = new TempDirectory();
            _exports = new TempDirectory();
            _settings = new TestSettings().Camera(new CameraOptions { Id = "cam1", Name = "Front door", Enabled = false, RtspUrl = "rtsp://127.0.0.1:1/cam1" });
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Export:Directory", _exports.Path);
            });
            _owner = TestSettings.Authenticate(_factory.CreateClient());
        }

        private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);
        private readonly TempDirectory _storage;
        private readonly TempDirectory _exports;
        private readonly TestSettings _settings;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _owner;

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private async Task<JsonElement> ReadyAsync(string id)
        {
            for (var attempt = 0; attempt < 300; attempt++)
            {
                var export = await ReadAsync(await _owner.GetAsync($"/api/exports/{id}"));
                if (export.GetProperty("status").GetString() is "Ready" or "Failed")
                {
                    return export;
                }

                await Task.Delay(200);
            }

            throw new TimeoutException("The export did not finish.");
        }

        [SkippableFact]
        public async Task RecordingsShouldBeExportedAsWatermarkedMp4AndShared()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");
            RealMedia.WriteRecordings(_storage.Path, "cam1", Start);

            // Bad requests first.
            Assert.Equal(HttpStatusCode.NotFound, (await _owner.PostAsJsonAsync("/api/exports", new { cameraId = "nope", from = Start, to = Start.AddSeconds(5) })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.PostAsJsonAsync("/api/exports", new { cameraId = "cam1", from = Start, to = Start.AddHours(2) })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.PostAsJsonAsync("/api/exports", new { cameraId = "cam1", from = Start.AddDays(1), to = Start.AddDays(1).AddSeconds(5) })).StatusCode);

            var queued = await _owner.PostAsJsonAsync("/api/exports", new { cameraId = "cam1", from = Start.AddSeconds(1), to = Start.AddSeconds(8), caseId = "case-1" });
            Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);
            var id = (await ReadAsync(queued)).GetProperty("id").GetString();

            var export = await ReadyAsync(id);
            Assert.True(export.GetProperty("status").GetString() == "Ready", export.ToString());
            Assert.Equal(("Front door", "case-1", "admin"), (export.GetProperty("cameraName").GetString(), export.GetProperty("caseId").GetString(), export.GetProperty("requestedBy").GetString()));
            var sha = export.GetProperty("sha256").GetString();

            // The file: H.264 and AAC in MP4, about as long as asked, and exactly what the SHA-256 says.
            var bytes = await _owner.GetByteArrayAsync($"/api/exports/{id}/video.mp4");
            Assert.Equal(export.GetProperty("size").GetInt64(), bytes.Length);
            Assert.Equal(sha, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            var file = Path.Combine(_storage.Path, "check.mp4");
            await File.WriteAllBytesAsync(file, bytes);
            var (streams, duration) = await RealMedia.ProbeAsync(file);
            Assert.Contains("h264", streams);
            Assert.Contains(streams, s => s.StartsWith("aac", StringComparison.Ordinal));
            Assert.InRange(duration, 5.5, 8.5);
            Assert.Equal("ftyp", System.Text.Encoding.ASCII.GetString(bytes, 4, 4));

            // Ranges, for players that seek.
            using (var range = new HttpRequestMessage(HttpMethod.Get, $"/api/exports/{id}/video.mp4"))
            {
                range.Headers.Range = new RangeHeaderValue(0, 99);
                var partial = await _owner.SendAsync(range);
                Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
                Assert.Equal(100, (await partial.Content.ReadAsByteArrayAsync()).Length);
            }

            // A share link downloads it without signing in, until revoked.
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.PostAsJsonAsync($"/api/exports/{id}/links", new { hours = 500 })).StatusCode);
            var created = await ReadAsync(await _owner.PostAsJsonAsync($"/api/exports/{id}/links", new { hours = 24 }));
            var path = created.GetProperty("path").GetString();
            Assert.InRange((created.GetProperty("expiresAt").GetDateTimeOffset() - DateTimeOffset.UtcNow).TotalHours, 23.9, 24.1);

            using (var anonymous = _factory.CreateClient())
            {
                Assert.Equal(bytes, await anonymous.GetByteArrayAsync(path));
                Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/shared/not-a-token/video.mp4")).StatusCode);
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/exports/{id}/video.mp4")).StatusCode);

                var links = await ReadAsync(await _owner.GetAsync($"/api/exports/{id}/links"));
                var link = Assert.Single(links.EnumerateArray());
                Assert.Equal(1, link.GetProperty("downloads").GetInt32());
                Assert.Equal(JsonValueKind.Null, link.GetProperty("path").ValueKind);

                Assert.Equal(HttpStatusCode.NoContent, (await _owner.DeleteAsync($"/api/exports/{id}/links/{link.GetProperty("id").GetString()}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(path)).StatusCode);
            }

            await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
            var actions = (await ReadAsync(await _owner.GetAsync("/api/audit?action=video"))).EnumerateArray().Select(e => e.GetProperty("action").GetString()).Reverse();
            Assert.Equal(new[] { "video.export", "video.share", "video.shared_download", "video.share_revoke" }, actions);
        }

        [Fact]
        public void TheWatermarkShouldShowTheSiteTimeOfEachFrame()
        {
            var text = FfmpegMp4Encoder.Text(new Watermark(new[] { "Onvif.Open · 100% Shops · Lobby", "Exported by\nadmin" }, Start, TimeSpan.FromHours(1), "UTC+01:00"));
            var lines = text.Split('\n');

            Assert.Equal(3, lines.Length);
            Assert.Equal("Onvif.Open · 100％ Shops · Lobby", lines[0]);
            Assert.Equal("Exported byadmin", lines[1]);
            Assert.Equal($"%{{pts:gmtime:{Start.AddHours(1).ToUnixTimeSeconds()}}} UTC+01:00", lines[2]);
        }

        public void Dispose()
        {
            _owner.Dispose();
            _factory.Dispose();
            _settings.Dispose();
            _storage.Dispose();
            _exports.Dispose();
        }
    }
}
