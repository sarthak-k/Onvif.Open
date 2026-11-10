// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    public class RecorderApiTest : IDisposable
    {
        #region Constructor

        public RecorderApiTest()
        {
            _storage = new TempDirectory();

            var day = Path.Combine(_storage.Path, "cam1", "2026-11-01");
            Directory.CreateDirectory(day);
            _segmentPath = Path.Combine(day, SegmentName);
            File.WriteAllBytes(_segmentPath, Enumerable.Repeat((byte)0x47, 188 * 3).ToArray());

            _settings = new TestSettings()
                .Camera(new Onvif.Open.Recorder.Configuration.CameraOptions { Id = "cam1", RtspUrl = "rtsp://admin:secret@127.0.0.1:1/stream", Username = "admin", Password = "secret" })
                .Camera(new Onvif.Open.Recorder.Configuration.CameraOptions { Id = "disabled", Enabled = false, RtspUrl = "rtsp://127.0.0.1:1/other" })
                .Camera(new Onvif.Open.Recorder.Configuration.CameraOptions { Id = "lobby", RtspUrl = "rtsp://127.0.0.1:1/lobby", Mode = Onvif.Open.Core.Recording.RecordingMode.Triggered });

            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("LiveView:StartTimeout", "00:00:00.500");
                builder.UseSetting("Recording:MinReconnectDelay", "00:00:00.050");
                builder.UseSetting("Recording:MaxReconnectDelay", "00:00:00.100");
            });

            _client = TestSettings.Authenticate(_factory.CreateClient());
        }

        #endregion

        #region Private Variables

        private const string SegmentName = "cam1_20261101T080000000Z.ts";

        private readonly TempDirectory _storage;
        private readonly TestSettings _settings;
        private readonly string _segmentPath;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _client;

        #endregion

        #region Public Methods

        [Fact]
        public async Task CamerasShouldListEnabledCamerasWithoutCredentials()
        {
            var body = await _client.GetStringAsync("/api/cameras");
            var cameras = JsonDocument.Parse(body).RootElement;

            Assert.Equal(2, cameras.GetArrayLength());
            Assert.Equal("cam1", cameras[0].GetProperty("cameraId").GetString());
            Assert.Equal("Continuous", cameras[0].GetProperty("mode").GetString());
            Assert.Equal("Triggered", cameras[1].GetProperty("mode").GetString());
            Assert.DoesNotContain("secret", body);

            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/disabled")).StatusCode);
        }

        [Fact]
        public async Task SegmentsShouldBeListedAndDownloadable()
        {
            var segments = JsonDocument.Parse(await _client.GetStringAsync("/api/cameras/cam1/segments")).RootElement;

            var segment = Assert.Single(segments.EnumerateArray());
            Assert.Equal(SegmentName, segment.GetProperty("fileName").GetString());
            Assert.Equal(188 * 3, segment.GetProperty("size").GetInt64());

            var download = await _client.GetAsync(segment.GetProperty("url").GetString());
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal("video/mp2t", download.Content.Headers.ContentType?.MediaType);
            Assert.Equal(File.ReadAllBytes(_segmentPath), await download.Content.ReadAsByteArrayAsync());

            var range = new HttpRequestMessage(HttpMethod.Get, segment.GetProperty("url").GetString());
            range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 187);
            Assert.Equal(HttpStatusCode.PartialContent, (await _client.SendAsync(range)).StatusCode);
        }

        [Theory]
        [InlineData("/api/cameras/cam1/segments/missing.ts")]
        [InlineData("/api/cameras/cam1/segments/..%2F..%2Fappsettings.json")]
        [InlineData("/api/cameras/..%2Fcam1/segments/" + SegmentName)]
        public async Task UnknownOrTraversalPathsShouldBeNotFound(string url)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync(url)).StatusCode);
        }

        [Fact]
        public async Task DeleteShouldRemoveSegment()
        {
            var response = await _client.DeleteAsync($"/api/cameras/cam1/segments/{SegmentName}");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(File.Exists(_segmentPath));
            Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/cameras/cam1/segments/{SegmentName}")).StatusCode);
        }

        [Fact]
        public async Task CameraCanBeStoppedAndStarted()
        {
            var stopped = JsonDocument.Parse(await (await _client.PostAsync("/api/cameras/cam1/stop", null)).Content.ReadAsStringAsync()).RootElement;
            Assert.Equal("Stopped", stopped.GetProperty("state").GetString());

            Assert.Equal(HttpStatusCode.Accepted, (await _client.PostAsync("/api/cameras/cam1/start", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/cameras/nope/start", null)).StatusCode);
        }

        [Fact]
        public async Task UnreachableCameraShouldReportError()
        {
            await Eventually.True(() =>
            {
                var camera = JsonDocument.Parse(_client.GetStringAsync("/api/cameras/cam1").Result).RootElement;
                return camera.GetProperty("lastError").ValueKind == JsonValueKind.String;
            }, "the connection error is reported");
        }

        [Fact]
        public async Task TriggerShouldApplyToTriggeredCamerasOnly()
        {
            var triggered = await _client.PostAsync("/api/cameras/lobby/trigger?seconds=60", null);
            Assert.Equal(HttpStatusCode.Accepted, triggered.StatusCode);
            Assert.True(JsonDocument.Parse(await triggered.Content.ReadAsStringAsync()).RootElement.GetProperty("isTriggered").GetBoolean());

            Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync("/api/cameras/cam1/trigger", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/cameras/nope/trigger", null)).StatusCode);

            var events = JsonDocument.Parse(await _client.GetStringAsync("/api/events?cameraId=lobby")).RootElement;
            Assert.Equal(JsonValueKind.Array, events.ValueKind);
        }

        [Fact]
        public async Task PtzShouldRequireAnOnvifCamera()
        {
            Assert.Equal(HttpStatusCode.NotImplemented, (await _client.GetAsync("/api/cameras/cam1/ptz")).StatusCode);
            Assert.Equal(HttpStatusCode.NotImplemented, (await _client.PostAsJsonAsync("/api/cameras/cam1/ptz/move", new { pan = 0.5 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.PostAsync("/api/cameras/nope/ptz/stop", null)).StatusCode);
        }

        [Fact]
        public async Task ImagingShouldRequireAnOnvifCamera()
        {
            Assert.Equal(HttpStatusCode.NotImplemented, (await _client.GetAsync("/api/cameras/cam1/imaging")).StatusCode);
            Assert.Equal(HttpStatusCode.NotImplemented, (await _client.PatchAsJsonAsync("/api/cameras/cam1/imaging", new { irCutFilter = "Auto" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/nope/imaging/options")).StatusCode);
        }

        [Fact]
        public async Task DeviceIoShouldRequireAnOnvifCamera()
        {
            Assert.Equal(HttpStatusCode.NotImplemented, (await _client.GetAsync("/api/cameras/cam1/io")).StatusCode);
            Assert.Equal(HttpStatusCode.NotImplemented, (await _client.PostAsJsonAsync("/api/cameras/cam1/io/relays/Relay_1", new { durationMs = 1000 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/nope/io")).StatusCode);
        }

        [Fact]
        public async Task DeviceRecordingsShouldRequireAnOnvifCamera()
        {
            Assert.Equal(HttpStatusCode.NotImplemented, (await _client.GetAsync("/api/cameras/cam1/device-recordings")).StatusCode);
            Assert.Equal(HttpStatusCode.NotImplemented, (await _client.GetAsync("/api/cameras/cam1/device-recordings/Rec_1/export?from=2026-11-05T08:00:00Z&to=2026-11-05T08:01:00Z")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/nope/device-recordings")).StatusCode);
        }

        [Fact]
        public async Task LiveViewShouldReportCamerasWithoutVideo()
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.GetAsync("/api/cameras/cam1/live.m3u8")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/cam1/live/0.ts")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/nope/live.m3u8")).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.GetAsync("/api/cameras/cam1/live.mp4")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/nope/live.mp4")).StatusCode);
        }

        [SkippableFact]
        public async Task LowLatencyLiveShouldStreamFragmentsAsFramesArrive()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            var live = _factory.Services.GetRequiredService<Onvif.Open.Recorder.Hosting.LiveViewService>();
            var frames = RealMedia.Frames.OfType<Onvif.Open.Core.Streaming.VideoFrame>().ToList();

            // Watched already (this test's start timeout is shorter than the camera's key frame interval).
            Assert.NotNull(live.WatchMp4("cam1"));
            using (var stop = new System.Threading.CancellationTokenSource())
            {
                // The camera keeps sending frames (at 25 fps) while the browser watches.
                var feed = Task.Run(async () =>
                {
                    foreach (var frame in frames)
                    {
                        live.OnFrame(this, new Onvif.Open.Core.Plugin.CameraFrame("cam1", frame, DateTimeOffset.UtcNow));
                        await Task.Delay(40, stop.Token);
                    }
                }, stop.Token);

                using (var response = await _client.GetAsync("/api/cameras/cam1/live.mp4", HttpCompletionOption.ResponseHeadersRead))
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
                    Assert.StartsWith("avc1.", response.Headers.GetValues("X-Video-Codec").Single());

                    // The init segment (ftyp, moov), then a fragment (moof) per frame, as they arrive.
                    var body = await response.Content.ReadAsStreamAsync();
                    var boxes = new List<string>();
                    var header = new byte[8];
                    while (boxes.Count(b => b == "moof") < 5)
                    {
                        await body.ReadExactlyAsync(header);
                        boxes.Add(System.Text.Encoding.ASCII.GetString(header, 4, 4));
                        var rest = new byte[System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header) - 8];
                        await body.ReadExactlyAsync(rest);
                    }

                    Assert.Equal(new[] { "ftyp", "moov", "moof", "mdat" }, boxes.Take(4));
                }

                stop.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => feed);
            }

            // The viewer left: nobody holds the stream open.
            await Eventually.True(() => live.WatchMp4("cam1").ViewerCount == 0, "the viewer was let go");
        }

        [Fact]
        public async Task RecordingsShouldBeListedAsHlsPlaylist()
        {
            var response = await _client.GetAsync("/api/cameras/cam1/recordings.m3u8?from=2026-11-01T07:00:00Z&to=2026-11-01T09:00:00Z");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("application/vnd.apple.mpegurl", response.Content.Headers.ContentType.MediaType);

            // Not a real MPEG-TS file: listed whole, pointing at the download endpoint (relative to the playlist).
            var playlist = await response.Content.ReadAsStringAsync();
            Assert.StartsWith("#EXTM3U", playlist);
            Assert.Contains("segments/" + SegmentName, playlist);
            Assert.DoesNotContain("BYTERANGE", playlist);

            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/cameras/cam1/recordings.m3u8?from=2026-11-01T09:00:00Z&to=2026-11-01T07:00:00Z")).StatusCode);
        }

        [Fact]
        public async Task ClipsShouldValidateTheirRange()
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/cameras/cam1/clip")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/cameras/cam1/clip?from=2026-11-01T00:00:00Z&to=2026-11-02T00:00:00Z")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/cam1/clip?from=2026-10-01T00:00:00Z&to=2026-10-01T01:00:00Z")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/cameras/nope/clip?from=2026-11-01T07:00:00Z&to=2026-11-01T09:00:00Z")).StatusCode);
        }

        [Fact]
        public async Task StorageAndHealthShouldRespond()
        {
            var storage = JsonDocument.Parse(await _client.GetStringAsync("/api/storage")).RootElement;
            Assert.Equal(188 * 3, storage.GetProperty("totalBytes").GetInt64());
            Assert.Equal(1, storage.GetProperty("segmentCount").GetInt32());
            Assert.True(storage.GetProperty("availableFreeBytes").GetInt64() > 0);

            var retention = await _client.PostAsync("/api/retention", null);
            Assert.Equal(HttpStatusCode.OK, retention.StatusCode);

            Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);
        }

        public void Dispose()
        {
            _client.Dispose();
            _factory.Dispose();
            _storage.Dispose();
            _settings.Dispose();
        }

        #endregion
    }
}
