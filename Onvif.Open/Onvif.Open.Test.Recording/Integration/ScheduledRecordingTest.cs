// Sarthak Killedar
// 2026-11-09

using System;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Hosting;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// "Schedule" policies: cameras record all the time during their hours and around events otherwise.
    /// </summary>
    public sealed class ScheduledRecordingTest : IDisposable
    {
        public ScheduledRecordingTest()
        {
            _storage = new TempDirectory();
        }

        private readonly TempDirectory _storage;

        [Fact]
        public async Task CamerasShouldRecordAllTheTimeDuringTheirHours()
        {
            // Saturday 10:00 UTC; the main site is in UTC.
            var clock = new ManualTimeProvider();
            using (var settings = new TestSettings().Camera(new CameraOptions { Id = "shop", RtspUrl = "rtsp://127.0.0.1:1/shop" }))
            using (var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock));
            }))
            using (var owner = TestSettings.Authenticate(factory.CreateClient()))
            {
                var invalid = await owner.PostAsJsonAsync("/api/recording-policies", new { name = "Shop", mode = "Schedule", schedule = Array.Empty<object>() });
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

                var created = await owner.PostAsJsonAsync("/api/recording-policies", new
                {
                    name = "Shop hours",
                    mode = "Schedule",
                    schedule = new[] { new { days = new[] { "Monday", "Tuesday" }, start = "09:00", end = "19:00" }, new { days = new[] { "Saturday" }, start = "10:00", end = "13:00" } }
                });
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                var policy = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement;
                Assert.Equal(2, policy.GetProperty("schedule").GetArrayLength());

                var camera = JsonDocument.Parse(await owner.GetStringAsync("/api/config/cameras/shop")).RootElement.Clone();
                var body = JsonSerializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(camera.GetRawText());
                body["policyId"] = policy.GetProperty("id").GetString();
                Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/config/cameras/shop", body)).StatusCode);

                var status = JsonDocument.Parse(await owner.GetStringAsync("/api/cameras/shop")).RootElement;
                Assert.Equal("Triggered", status.GetProperty("mode").GetString());

                // Saturday 10:00: within its hours, it records.
                await factory.Services.GetRequiredService<ScheduledRecording>().CheckAsync();
                Assert.True(JsonDocument.Parse(await owner.GetStringAsync("/api/cameras/shop")).RootElement.GetProperty("isTriggered").GetBoolean());

                // A video policy with hours keeps continuous video like "Always record", and event video longer.
                Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/recording-policies/{policy.GetProperty("id").GetString()}", new { continuousRetentionDays = 40 })).StatusCode);
            }
        }

        public void Dispose()
        {
            _storage.Dispose();
        }
    }
}
