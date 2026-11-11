// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Onvif.Open.Recorder.Mail;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// The cloud: one region's control plane, where customers sign up themselves with a verified email, each in their own
    /// organization, and where this recorder never connects to an address a customer gives it.
    /// </summary>
    public sealed class CloudSignupTest : IDisposable
    {
        public CloudSignupTest()
        {
            _settings = new TestSettings();
            _storage = new TempDirectory();
            _factory = Cloud(builder => { });
            _anonymous = _factory.CreateClient();
        }

        public void Dispose()
        {
            _anonymous.Dispose();
            _factory.Dispose();
            _settings.Dispose();
            _storage.Dispose();
        }

        private readonly TestSettings _settings;
        private readonly TempDirectory _storage;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _anonymous;
        private readonly FakeMail _mail = new FakeMail();

        private sealed class FakeMail : IMailSender
        {
            public bool IsConfigured { get; set; } = true;
            public string From => "Onvif.Open <no-reply@example.com>";
            public ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new ConcurrentQueue<(string, string, string)>();

            public Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
            {
                Sent.Enqueue((message.To.Single().Address, message.Subject, message.Body));
                return Task.CompletedTask;
            }
        }

        private WebApplicationFactory<Program> Cloud(Action<Microsoft.AspNetCore.Hosting.IWebHostBuilder> more) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Cloud:Enabled", "true");
                builder.UseSetting("Cloud:Region", "us");
                builder.UseSetting("Cloud:Regions:us:WebUrl", "https://us.cameras.example.com/");
                builder.UseSetting("Cloud:Regions:asia:WebUrl", "https://asia.cameras.example.com");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IMailSender>();
                    services.AddSingleton<IMailSender>(_mail);
                });
                more(builder);
            });

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private static object Signup(string email, string password = "Shop-Owner-Pass-1") =>
            new { email, password, organizationName = "Riverside Shops", siteName = "Main St", timeZone = "America/New_York" };

        private string TokenSentTo(string email)
        {
            var mail = _mail.Sent.Last(m => m.To == email);
            return Regex.Match(mail.Body, @"/signup/verify\?token=([A-Za-z0-9_-]+)").Groups[1].Value;
        }

        [Fact]
        public async Task ACustomerShouldSignUpWithAVerifiedEmailIntoTheirOwnOrganization()
        {
            // Where this is, and where the other region is.
            var cloud = await ReadAsync(await _anonymous.GetAsync("/api/cloud"));
            Assert.Equal((true, "us", true), (cloud.GetProperty("enabled").GetBoolean(), cloud.GetProperty("region").GetString(), cloud.GetProperty("signup").GetBoolean()));
            Assert.Equal(new[] { "https://asia.cameras.example.com", "https://us.cameras.example.com" }, cloud.GetProperty("regions").EnumerateArray().Select(r => r.GetProperty("webUrl").GetString()).OrderBy(u => u));
            Assert.False((await ReadAsync(await _anonymous.GetAsync("/api/setup"))).GetProperty("required").GetBoolean());

            foreach (var bad in new[] { Signup("not an email"), Signup("ada@example.com", "short"), new { email = "ada@example.com", password = "Shop-Owner-Pass-1", organizationName = "", siteName = "Main", timeZone = "Mars/Olympus" } })
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await _anonymous.PostAsJsonAsync("/api/signup", bad)).StatusCode);
            }

            // Signing up sends a link; nothing exists until it is opened, and the link's token is not stored.
            Assert.Equal(HttpStatusCode.Accepted, (await _anonymous.PostAsJsonAsync("/api/signup", Signup("ada@example.com"))).StatusCode);
            var token = TokenSentTo("ada@example.com");
            Assert.Contains("https://us.cameras.example.com/signup/verify?token=", _mail.Sent.Last().Body);
            using (var db = _settings.Contexts.CreateDbContext())
            {
                var pending = Assert.Single(db.Signups);
                Assert.NotEqual(token, pending.TokenHash);
                Assert.DoesNotContain("Shop-Owner-Pass-1", pending.Password);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await _anonymous.PostAsJsonAsync("/api/signup/verify", new { token = "not-it" })).StatusCode);
            var verified = await _anonymous.PostAsJsonAsync("/api/signup/verify", new { token });
            Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
            var owner = _factory.CreateClient();
            owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await ReadAsync(verified)).GetProperty("accessToken").GetString());

            // Their own organization, in this region, with its site and the default policies; they own it.
            var me = await ReadAsync(await owner.GetAsync("/api/auth/me"));
            Assert.Equal(("ada@example.com", "Owner"), (me.GetProperty("userName").GetString(), me.GetProperty("role").GetString()));
            var organization = await ReadAsync(await owner.GetAsync("/api/organization"));
            Assert.Equal(("Riverside Shops", "us"), (organization.GetProperty("name").GetString(), organization.GetProperty("region").GetString()));
            Assert.NotEqual(TestSettings.OrganizationId, organization.GetProperty("id").GetString());
            Assert.Equal("Main St", Assert.Single((await ReadAsync(await owner.GetAsync("/api/sites"))).EnumerateArray()).GetProperty("name").GetString());
            Assert.NotEmpty((await ReadAsync(await owner.GetAsync("/api/recording-policies"))).EnumerateArray());

            // Nothing of the other organizations.
            Assert.Single((await ReadAsync(await owner.GetAsync("/api/users"))).EnumerateArray());
            Assert.Empty((await ReadAsync(await owner.GetAsync("/api/cameras"))).EnumerateArray());
            Assert.Empty((await ReadAsync(await owner.GetAsync("/api/gateways"))).EnumerateArray());

            // The link works once.
            Assert.Equal(HttpStatusCode.NotFound, (await _anonymous.PostAsJsonAsync("/api/signup/verify", new { token })).StatusCode);

            // Signing up again with that email looks the same, but the email says there is an account already.
            Assert.Equal(HttpStatusCode.Accepted, (await _anonymous.PostAsJsonAsync("/api/signup", Signup("ada@example.com"))).StatusCode);
            Assert.Contains("already has an account", _mail.Sent.Last().Body);
            using (var db = _settings.Contexts.CreateDbContext())
            {
                Assert.Empty(db.Signups);
            }

            await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
            var audit = await ReadAsync(await owner.GetAsync("/api/audit?action=organization"));
            Assert.Equal("organization.signup", audit[0].GetProperty("action").GetString());
        }

        [Fact]
        public async Task TheCloudShouldNeverConnectToACustomersAddressItself()
        {
            Assert.Equal(HttpStatusCode.Accepted, (await _anonymous.PostAsJsonAsync("/api/signup", Signup("ben@example.com"))).StatusCode);
            var verified = await ReadAsync(await _anonymous.PostAsJsonAsync("/api/signup/verify", new { token = TokenSentTo("ben@example.com") }));
            var owner = _factory.CreateClient();
            owner.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", verified.GetProperty("accessToken").GetString());
            var site = (await ReadAsync(await owner.GetAsync("/api/sites")))[0].GetProperty("id").GetString();

            // No looking around its network, no testing addresses from here, no cameras recorded here.
            Assert.Equal(HttpStatusCode.NotImplemented, (await owner.GetAsync("/api/discovery")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/config/cameras/test", new { rtspUrl = "rtsp://169.254.169.254/latest" })).StatusCode);
            var added = await owner.PostAsJsonAsync("/api/config/cameras", new { name = "Door", siteId = site, rtspUrl = "rtsp://10.0.0.5/stream" });
            Assert.Equal(HttpStatusCode.BadRequest, added.StatusCode);
            Assert.Contains("Choose the gateway", await added.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task LinksShouldExpire()
        {
            using (var factory = Cloud(builder => builder.UseSetting("Cloud:SignupLifetime", "00:00:01")))
            using (var client = factory.CreateClient())
            {
                Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/signup", Signup("cleo@example.com"))).StatusCode);
                var token = TokenSentTo("cleo@example.com");
                await Task.Delay(1500);
                Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/signup/verify", new { token })).StatusCode);
            }
        }

        [Fact]
        public async Task SignupShouldNeedAMailServerAndAValidRegionConfiguration()
        {
            _mail.IsConfigured = false;
            Assert.False((await ReadAsync(await _anonymous.GetAsync("/api/cloud"))).GetProperty("signup").GetBoolean());
            Assert.Equal(HttpStatusCode.NotFound, (await _anonymous.PostAsJsonAsync("/api/signup", Signup("dan@example.com"))).StatusCode);

            using (var broken = Cloud(builder => builder.UseSetting("Cloud:Region", "mars")))
            {
                var error = Assert.ThrowsAny<Exception>(() => broken.CreateClient());
                Assert.Contains("Cloud:Region", error.ToString());
            }
        }
    }
}
