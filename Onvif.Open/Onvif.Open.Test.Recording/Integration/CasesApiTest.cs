// Sarthak Killedar
// 2026-11-09

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Cases: clips of video with notes, who may do what, and the video of open cases being kept.
    /// </summary>
    public sealed class CasesApiTest : IDisposable
    {
        public CasesApiTest()
        {
            _storage = new TempDirectory();
            _settings = new TestSettings();
            _lyon = _settings.AddSite("Lyon");
            _settings.Camera(new CameraOptions { Id = "lobby", Name = "Lobby", Enabled = false, RtspUrl = "rtsp://127.0.0.1:1/lobby" })
                     .Camera(new CameraOptions { Id = "hall", Name = "Hall", Enabled = false, RtspUrl = "rtsp://127.0.0.1:1/hall" })
                     .Camera(new CameraOptions { Id = "dock", Name = "Dock", SiteId = _lyon, Enabled = false, RtspUrl = "rtsp://127.0.0.1:1/dock" });
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
            });
            _owner = TestSettings.Authenticate(_factory.CreateClient());
        }

        private readonly TempDirectory _storage;
        private readonly TestSettings _settings;
        private readonly string _lyon;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _owner;

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private async Task<HttpClient> MemberAsync(string userName, string role, params string[] sites)
        {
            Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/users", new { userName, password = "Temporary-Pass-1", role, sites })).StatusCode);
            var client = _factory.CreateClient();
            var first = await TestSettings.SignInAsync(client, userName, "Temporary-Pass-1");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
            var changed = await client.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Temporary-Pass-1", newPassword = userName + "-Own-Pass-2" });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await ReadAsync(changed)).GetProperty("accessToken").GetString());
            return client;
        }

        private static RecordingSegment Segment(string camera, DateTimeOffset start) =>
            new RecordingSegment(camera, $"/r/{camera}/{start:yyyyMMddHHmmss}.ts", start, start.AddMinutes(10), 1000, isActive: false);

        [Fact]
        public async Task OperatorsShouldKeepClipsOfAnIncidentInACase()
        {
            using (var operatorClient = await MemberAsync("oscar", "Operator"))
            using (var viewer = await MemberAsync("vera", "Viewer"))
            using (var lyonViewer = await MemberAsync("lena", "Viewer", _lyon))
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/cases", new { name = "Mine" })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PostAsJsonAsync("/api/cases", new { name = " " })).StatusCode);

                var created = await operatorClient.PostAsJsonAsync("/api/cases", new { name = "Break-in at the back door", description = "Reported by the night guard." });
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                var caseId = (await ReadAsync(created)).GetProperty("id").GetString();

                var at = DateTimeOffset.UtcNow.AddHours(-2);
                var clip = await operatorClient.PostAsJsonAsync($"/api/cases/{caseId}/clips", new { cameraIds = new[] { "lobby", "hall" }, from = at, to = at.AddMinutes(3), note = "Two people enter", eventId = "e1" });
                Assert.Equal(HttpStatusCode.Created, clip.StatusCode);
                var clipId = (await ReadAsync(clip)).GetProperty("id").GetString();
                Assert.Equal(HttpStatusCode.Created, (await operatorClient.PostAsJsonAsync($"/api/cases/{caseId}/clips", new { cameraIds = new[] { "dock" }, from = at, to = at.AddMinutes(1) })).StatusCode);

                // Clips are at one site, end after they start, last at most 6 hours, and show known cameras.
                Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PostAsJsonAsync($"/api/cases/{caseId}/clips", new { cameraIds = new[] { "lobby", "dock" }, from = at, to = at.AddMinutes(1) })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PostAsJsonAsync($"/api/cases/{caseId}/clips", new { cameraIds = new[] { "lobby" }, from = at, to = at })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PostAsJsonAsync($"/api/cases/{caseId}/clips", new { cameraIds = new[] { "lobby" }, from = at, to = at.AddHours(7) })).StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PostAsJsonAsync($"/api/cases/{caseId}/clips", new { cameraIds = new[] { "nope" }, from = at, to = at.AddMinutes(1) })).StatusCode);

                var note = await operatorClient.PutAsJsonAsync($"/api/cases/{caseId}/clips/{clipId}", new { note = "Two people enter by the back door" });
                Assert.Equal(HttpStatusCode.OK, note.StatusCode);

                // Everyone reads cases, with the clips of their sites.
                var full = await ReadAsync(await viewer.GetAsync($"/api/cases/{caseId}"));
                Assert.Equal(2, full.GetProperty("clips").GetArrayLength());
                var first = full.GetProperty("clips")[0];
                Assert.Equal(new[] { "Lobby", "Hall" }, first.GetProperty("cameras").EnumerateArray().Select(c => c.GetProperty("name").GetString()));
                Assert.Equal(("Two people enter by the back door", "oscar"), (first.GetProperty("note").GetString(), first.GetProperty("createdBy").GetString()));
                var lyon = await ReadAsync(await lyonViewer.GetAsync($"/api/cases/{caseId}"));
                Assert.Equal(new[] { "dock" }, lyon.GetProperty("clips").EnumerateArray().Select(c => c.GetProperty("cameraIds")[0].GetString()));
                Assert.Equal(HttpStatusCode.Forbidden, (await lyonViewer.DeleteAsync($"/api/cases/{caseId}/clips/{clipId}")).StatusCode);

                var list = await ReadAsync(await viewer.GetAsync("/api/cases"));
                Assert.Equal(("Break-in at the back door", 2, "Open"), (list[0].GetProperty("name").GetString(), list[0].GetProperty("clipCount").GetInt32(), list[0].GetProperty("status").GetString()));

                // Closed: no more clips, until it is reopened.
                Assert.Equal(HttpStatusCode.OK, (await operatorClient.PutAsJsonAsync($"/api/cases/{caseId}", new { status = "Closed" })).StatusCode);
                Assert.Empty((await ReadAsync(await viewer.GetAsync("/api/cases?status=open"))).EnumerateArray());
                Assert.Equal(HttpStatusCode.Conflict, (await operatorClient.PostAsJsonAsync($"/api/cases/{caseId}/clips", new { cameraIds = new[] { "lobby" }, from = at, to = at.AddMinutes(1) })).StatusCode);
                Assert.Equal(HttpStatusCode.OK, (await operatorClient.PutAsJsonAsync($"/api/cases/{caseId}", new { status = "Open" })).StatusCode);

                // Only administrators delete cases.
                Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync($"/api/cases/{caseId}")).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await operatorClient.DeleteAsync($"/api/cases/{caseId}/clips/{clipId}")).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await _owner.DeleteAsync($"/api/cases/{caseId}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/cases/{caseId}")).StatusCode);

                await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
                var actions = (await ReadAsync(await _owner.GetAsync("/api/audit?action=case"))).EnumerateArray().Select(e => e.GetProperty("action").GetString()).Reverse();
                Assert.Equal(new[] { "case.create", "case.clip_add", "case.clip_add", "case.clip_update", "case.update", "case.update", "case.clip_remove", "case.delete" }, actions);
            }
        }

        [Fact]
        public async Task VideoOfOpenCasesShouldBeKept()
        {
            var retention = _factory.Services.GetRequiredService<PolicyRetention>();
            var now = DateTimeOffset.UtcNow;
            var old = now.AddDays(-45);
            var evidence = Segment("lobby", old);
            var other = Segment("lobby", old.AddHours(1));

            // Older than the policy keeps anything.
            Assert.True(retention.IsExpired(evidence, now));

            var caseId = (await ReadAsync(await _owner.PostAsJsonAsync("/api/cases", new { name = "Old theft" }))).GetProperty("id").GetString();
            Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync($"/api/cases/{caseId}/clips", new { cameraIds = new[] { "lobby" }, from = old.AddMinutes(2), to = old.AddMinutes(4) })).StatusCode);

            Assert.False(retention.IsExpired(evidence, now));
            Assert.True(retention.IsExpired(other, now));
            Assert.True(retention.IsExpired(Segment("hall", old), now));

            // Closing the case lets its video go.
            Assert.Equal(HttpStatusCode.OK, (await _owner.PutAsJsonAsync($"/api/cases/{caseId}", new { status = "Closed" })).StatusCode);
            Assert.True(retention.IsExpired(evidence, now));
        }

        public void Dispose()
        {
            _owner.Dispose();
            _factory.Dispose();
            _settings.Dispose();
            _storage.Dispose();
        }
    }
}
