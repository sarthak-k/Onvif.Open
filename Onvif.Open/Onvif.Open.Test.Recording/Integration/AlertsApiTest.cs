// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Alerts;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Alerts: rules, who may set them up, what raises an alert (events, cameras offline for a while), the cooldown,
    /// the inbox and acknowledging, and notifications by email and signed webhook.
    /// </summary>
    public sealed class AlertsApiTest : IDisposable
    {
        public AlertsApiTest()
        {
            _storage = new TempDirectory();
            _settings = new TestSettings();
            _lyon = _settings.AddSite("Lyon");
            _settings.Camera(new CameraOptions { Id = "lobby", Name = "Lobby", Enabled = false, RtspUrl = "rtsp://127.0.0.1:1/lobby" })
                     .Camera(new CameraOptions { Id = "dock", Name = "Dock", SiteId = _lyon, Enabled = false, RtspUrl = "rtsp://127.0.0.1:1/dock" });
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Alerts:PublicUrl", "https://nvr.example.com/");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IAlertNotifier>();
                    services.AddSingleton<IAlertNotifier>(new EmailNotifier(new SmtpOptions { Host = "smtp.example.com", From = "alerts@example.com" }, (message, _) =>
                    {
                        _mails.Enqueue((message.Subject, string.Join(",", message.To.Select(t => t.Address)), message.Body));
                        return Task.CompletedTask;
                    }));
                    services.AddSingleton<IAlertNotifier>(new WebhookNotifier(new HttpClient(new Hook(_hooks))));
                });
            });
            _owner = TestSettings.Authenticate(_factory.CreateClient());
        }

        public void Dispose()
        {
            _owner.Dispose();
            _factory.Dispose();
            _settings.Dispose();
            _storage.Dispose();
        }

        private readonly TempDirectory _storage;
        private readonly TestSettings _settings;
        private readonly string _lyon;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _owner;
        private readonly ConcurrentQueue<(string Subject, string To, string Body)> _mails = new ConcurrentQueue<(string, string, string)>();
        private readonly ConcurrentQueue<(string Url, string Signature, string Body)> _hooks = new ConcurrentQueue<(string, string, string)>();

        private sealed class Hook : HttpMessageHandler
        {
            public Hook(ConcurrentQueue<(string, string, string)> received) => _received = received;

            private readonly ConcurrentQueue<(string, string, string)> _received;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                _received.Enqueue((request.RequestUri.ToString(), request.Headers.TryGetValues(WebhookNotifier.SignatureHeader, out var values) ? values.Single() : null, body));
                return new HttpResponseMessage(request.RequestUri.Host == "down.example.com" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
            }
        }

        private AlertEngine Engine => _factory.Services.GetRequiredService<AlertEngine>();

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

        private async Task<string> RuleAsync(object rule)
        {
            var created = await _owner.PostAsJsonAsync("/api/alert-rules", rule);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            return (await ReadAsync(created)).GetProperty("id").GetString();
        }

        private static EventRecord Event(string camera, string site, string category, string severity, DateTime start) => new EventRecord
        {
            Id = Guid.NewGuid().ToString("D"),
            OrganizationId = TestSettings.OrganizationId,
            SiteId = site,
            CameraId = camera,
            Category = category,
            Severity = severity,
            Title = char.ToUpperInvariant(category[0]) + category.Substring(1),
            Source = "detection",
            StartTime = start
        };

        [Fact]
        public async Task OrganizationAdministratorsShouldSetUpRules()
        {
            using (var admin = await MemberAsync("ada", "Admin"))
            using (var lyonAdmin = await MemberAsync("leo", "Admin", _lyon))
            using (var operatorClient = await MemberAsync("oscar", "Operator"))
            {
                var rule = new { name = "Person at night", categories = new[] { "person" }, emails = new[] { "guard@example.com" } };
                Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync("/api/alert-rules", rule)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden, (await lyonAdmin.GetAsync("/api/alert-rules")).StatusCode);

                var created = await admin.PostAsJsonAsync("/api/alert-rules", new
                {
                    name = "Person at night",
                    categories = new[] { "Person" },
                    minSeverity = "warning",
                    siteIds = new[] { _lyon },
                    hours = new[] { new { days = new[] { "Monday", "Tuesday" }, start = "22:00", end = "06:00" } },
                    emails = new[] { "guard@example.com" },
                    webhookUrl = "https://hooks.example.com/alerts",
                    webhookSecret = "s3cret"
                });
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                var body = await ReadAsync(created);
                var id = body.GetProperty("id").GetString();
                Assert.Equal(("person", "Warning", true, "Event"), (body.GetProperty("categories")[0].GetString(), body.GetProperty("minSeverity").GetString(), body.GetProperty("hasWebhookSecret").GetBoolean(), body.GetProperty("kind").GetString()));
                Assert.DoesNotContain("s3cret", await (await admin.GetAsync($"/api/alert-rules/{id}")).Content.ReadAsStringAsync());

                // A null secret keeps it, an empty one removes it.
                var kept = await ReadAsync(await admin.PutAsJsonAsync($"/api/alert-rules/{id}", new { name = "Person at night", categories = new[] { "person" }, webhookUrl = "https://hooks.example.com/alerts", enabled = false }));
                Assert.Equal((true, false), (kept.GetProperty("hasWebhookSecret").GetBoolean(), kept.GetProperty("enabled").GetBoolean()));
                var cleared = await ReadAsync(await admin.PutAsJsonAsync($"/api/alert-rules/{id}", new { name = "Person at night", webhookUrl = "https://hooks.example.com/alerts", webhookSecret = "" }));
                Assert.False(cleared.GetProperty("hasWebhookSecret").GetBoolean());

                foreach (var bad in new object[]
                {
                    new { name = " " },
                    new { name = "x", kind = "Weather" },
                    new { name = "x", categories = new[] { "aliens" } },
                    new { name = "x", minSeverity = "Huge" },
                    new { name = "x", kind = "Offline", offlineMinutes = 0 },
                    new { name = "x", siteIds = new[] { "nowhere" } },
                    new { name = "x", cameraIds = new[] { "nope" } },
                    new { name = "x", hours = new[] { new { days = new[] { "Monday" }, start = "25:00", end = "06:00" } } },
                    new { name = "x", emails = new[] { "not an address" } },
                    new { name = "x", webhookUrl = "ftp://example.com/x" }
                })
                {
                    Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/alert-rules", bad)).StatusCode);
                }

                Assert.Single((await ReadAsync(await admin.GetAsync("/api/alert-rules"))).EnumerateArray());
                Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/alert-rules/{id}")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/alert-rules/{id}")).StatusCode);

                await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
                var actions = (await ReadAsync(await _owner.GetAsync("/api/audit?action=alert"))).EnumerateArray().Select(e => e.GetProperty("action").GetString()).Reverse();
                Assert.Equal(new[] { "alert.rule_create", "alert.rule_update", "alert.rule_update", "alert.rule_delete" }, actions);
            }
        }

        [Fact]
        public async Task EventsShouldRaiseAlertsThatAreDeliveredAndAcknowledged()
        {
            await RuleAsync(new
            {
                name = "People at Lyon",
                categories = new[] { "person", "intrusion" },
                siteIds = new[] { _lyon },
                cooldownMinutes = 10,
                emails = new[] { "guard@example.com" },
                webhookUrl = "https://hooks.example.com/alerts",
                webhookSecret = "s3cret"
            });
            await RuleAsync(new { name = "Critical anywhere", minSeverity = "Critical", webhookUrl = "https://down.example.com/x" });

            var now = DateTime.UtcNow;
            var person = Event("dock", _lyon, "person", "Info", now);
            var raised = await Engine.HandleAsync(person);
            Assert.Equal("People at Lyon", Assert.Single(raised).RuleName);

            // Not the categories, not the site, too soon after the last one (cooldown), or not severe enough.
            Assert.Empty(await Engine.HandleAsync(Event("dock", _lyon, "motion", "Info", now)));
            Assert.Empty(await Engine.HandleAsync(Event("lobby", TestSettings.SiteId, "person", "Info", now)));
            Assert.Empty(await Engine.HandleAsync(Event("dock", _lyon, "intrusion", "Warning", now.AddMinutes(1))));

            // Delivered: an email, and a webhook signed with the rule's secret.
            var mail = Assert.Single(_mails);
            Assert.Equal(("[Onvif.Open] Person at Dock (Lyon)", "guard@example.com"), (mail.Subject, mail.To));
            Assert.Contains("https://nvr.example.com/alerts", mail.Body);
            var hook = Assert.Single(_hooks);
            Assert.Equal("https://hooks.example.com/alerts", hook.Url);
            Assert.Equal(WebhookNotifier.Sign("s3cret", System.Text.Encoding.UTF8.GetBytes(hook.Body)), hook.Signature);
            var payload = JsonDocument.Parse(hook.Body).RootElement;
            Assert.Equal(("alert", "person", "Dock", person.Id), (payload.GetProperty("type").GetString(), payload.GetProperty("category").GetString(), payload.GetProperty("camera").GetProperty("name").GetString(), payload.GetProperty("eventId").GetString()));

            // A failed notification is noted on the alert.
            var critical = Assert.Single(await Engine.HandleAsync(Event("lobby", TestSettings.SiteId, "tamper", "Critical", now.AddSeconds(1))));
            Assert.Contains("503", AlertDelivery.Read(critical.Delivery)["webhook"].Error);

            using (var lyonViewer = await MemberAsync("lena", "Viewer", _lyon))
            using (var lyonOperator = await MemberAsync("otto", "Operator", _lyon))
            {
                // Everyone sees the alerts of their sites.
                var all = await ReadAsync(await _owner.GetAsync("/api/alerts?status=open"));
                Assert.Equal(new[] { "Tamper", "Person" }, all.EnumerateArray().Select(a => a.GetProperty("title").GetString()));
                Assert.True(all[1].GetProperty("delivery").GetProperty("email").GetProperty("ok").GetBoolean());
                var lyon = await ReadAsync(await lyonViewer.GetAsync("/api/alerts"));
                var alertId = Assert.Single(lyon.EnumerateArray()).GetProperty("id").GetString();
                var summary = await ReadAsync(await lyonViewer.GetAsync("/api/alerts/summary"));
                Assert.Equal((1, alertId), (summary.GetProperty("open").GetInt32(), summary.GetProperty("latest").GetProperty("id").GetString()));

                // Operators acknowledge the alerts they see.
                Assert.Equal(HttpStatusCode.Forbidden, (await lyonViewer.PostAsync($"/api/alerts/{alertId}/acknowledge", null)).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await lyonOperator.PostAsync($"/api/alerts/{critical.Id}/acknowledge", null)).StatusCode);
                var acknowledged = await ReadAsync(await lyonOperator.PostAsync($"/api/alerts/{alertId}/acknowledge", null));
                Assert.Equal("otto", acknowledged.GetProperty("acknowledgedBy").GetString());
                Assert.Equal(0, (await ReadAsync(await lyonViewer.GetAsync("/api/alerts/summary"))).GetProperty("open").GetInt32());
                Assert.Single((await ReadAsync(await lyonViewer.GetAsync("/api/alerts"))).EnumerateArray());
            }
        }

        [Fact]
        public async Task RulesWithHoursShouldAlertOnlyThen()
        {
            var today = DateTime.UtcNow.DayOfWeek;
            await RuleAsync(new { name = "Out of hours", hours = new[] { new { days = new[] { today.ToString() }, start = "22:00", end = "06:00" } } });

            var day = DateTime.UtcNow.Date;
            Assert.Empty(await Engine.HandleAsync(Event("lobby", TestSettings.SiteId, "motion", "Info", day.AddHours(12))));
            Assert.Single(await Engine.HandleAsync(Event("lobby", TestSettings.SiteId, "motion", "Info", day.AddHours(23))));
        }

        [Fact]
        public async Task CamerasOfflineForAWhileShouldAlertOncePerOutage()
        {
            await RuleAsync(new { name = "Offline 5 min", kind = "Offline", offlineMinutes = 5, cooldownMinutes = 0, cameraIds = new[] { "lobby" } });

            // The camera just went offline: not yet.
            var now = DateTime.UtcNow;
            await Engine.HandleAsync(Event("lobby", TestSettings.SiteId, EventCategories.Offline, "Warning", now.AddMinutes(-2)));
            await Engine.HandleAsync(Event("dock", _lyon, EventCategories.Offline, "Warning", now.AddMinutes(-20)));
            Assert.Empty(await Engine.CheckOfflineAsync());

            // Offline for long enough: once, until it records again.
            Engine.CameraRecovered("lobby");
            await Engine.HandleAsync(Event("lobby", TestSettings.SiteId, EventCategories.Offline, "Warning", now.AddMinutes(-6)));
            var alert = Assert.Single(await Engine.CheckOfflineAsync());
            Assert.Equal(("Camera offline for 6 min", "Lobby"), (alert.Title, alert.CameraName));
            Assert.Empty(await Engine.CheckOfflineAsync());

            Engine.CameraRecovered("lobby");
            await Engine.HandleAsync(Event("lobby", TestSettings.SiteId, EventCategories.Offline, "Warning", now.AddMinutes(-7)));
            Assert.Single(await Engine.CheckOfflineAsync());
        }

        [Fact]
        public async Task WhatTheRecorderReportsShouldReachTheRules()
        {
            await RuleAsync(new { name = "Offline now", categories = new[] { "offline" } });

            var recorder = _factory.Services.GetServices<IHostedService>().OfType<EventRecorder>().Single();
            await Task.Delay(200);
            recorder.OnStateChanged(this, new CameraStateChange("lobby", RecordingState.Recording, RecordingState.Reconnecting, "Connection refused", DateTimeOffset.UtcNow));

            JsonElement alerts = default;
            for (var i = 0; i < 100 && (alerts.ValueKind != JsonValueKind.Array || alerts.GetArrayLength() == 0); i++)
            {
                await Task.Delay(100);
                alerts = await ReadAsync(await _owner.GetAsync("/api/alerts"));
            }

            Assert.Equal(("Camera offline", "Lobby"), (alerts[0].GetProperty("title").GetString(), alerts[0].GetProperty("cameraName").GetString()));
        }

        [Fact]
        public async Task RulesShouldBeTestedWithoutAnAlert()
        {
            var id = await RuleAsync(new { name = "Try me", emails = new[] { "guard@example.com" }, webhookUrl = "https://down.example.com/x" });
            var tested = await ReadAsync(await _owner.PostAsync($"/api/alert-rules/{id}/test", null));
            Assert.True(tested.GetProperty("delivery").GetProperty("email").GetProperty("ok").GetBoolean());
            Assert.False(tested.GetProperty("delivery").GetProperty("webhook").GetProperty("ok").GetBoolean());
            Assert.Equal("[Onvif.Open] Test alert", Assert.Single(_mails).Subject);
            Assert.Empty((await ReadAsync(await _owner.GetAsync("/api/alerts"))).EnumerateArray());
        }
    }
}
