// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Onvif.Open.Recorder.Billing;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Plans from the configuration, the trial, the camera limit, and paying through Stripe (its SDK, against Stripe's
    /// API in memory) with signed webhooks moving the subscription.
    /// </summary>
    public sealed class BillingApiTest : IDisposable
    {
        public BillingApiTest()
        {
            _settings = new TestSettings();
            _storage = new TempDirectory();
            _factory = Billing(builder => { });
            _owner = TestSettings.Authenticate(_factory.CreateClient());
        }

        public void Dispose()
        {
            _owner.Dispose();
            _factory.Dispose();
            _settings.Dispose();
            _storage.Dispose();
        }

        private const string WebhookSecret = "whsec_test_secret";

        private readonly TestSettings _settings;
        private readonly TempDirectory _storage;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _owner;
        private readonly FakeStripe _stripe = new FakeStripe();

        /// <summary>Stripe's API: checkout and portal sessions, and what was asked.</summary>
        private sealed class FakeStripe : HttpMessageHandler
        {
            public ConcurrentQueue<(string Path, Dictionary<string, string> Form, string Key)> Calls { get; } = new();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var form = HttpUtility.ParseQueryString(await request.Content.ReadAsStringAsync(cancellationToken));
                Calls.Enqueue((request.RequestUri.AbsolutePath, form.AllKeys.ToDictionary(k => k, k => form[k]), request.Headers.Authorization?.Parameter));
                var body = request.RequestUri.AbsolutePath switch
                {
                    "/v1/checkout/sessions" => new { id = "cs_1", @object = "checkout.session", url = "https://checkout.stripe.test/cs_1" },
                    "/v1/billing_portal/sessions" => (object)new { id = "bps_1", @object = "billing_portal.session", url = "https://billing.stripe.test/p/bps_1" },
                    _ => null
                };
                return body == null
                    ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":{\"message\":\"No such route\"}}") }
                    : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
            }
        }

        private WebApplicationFactory<Program> Billing(Action<IWebHostBuilder> more) =>
            new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.UseSetting("Billing:Enabled", "true");
                builder.UseSetting("Billing:Currency", "EUR");
                builder.UseSetting("Billing:TrialPlan", "trial");
                builder.UseSetting("Billing:TrialDays", "14");
                builder.UseSetting("Billing:Plans:0:Id", "trial");
                builder.UseSetting("Billing:Plans:0:Name", "Trial");
                builder.UseSetting("Billing:Plans:0:Cameras", "2");
                builder.UseSetting("Billing:Plans:0:StorageGB", "10");
                builder.UseSetting("Billing:Plans:1:Id", "starter");
                builder.UseSetting("Billing:Plans:1:Name", "Starter");
                builder.UseSetting("Billing:Plans:1:Cameras", "4");
                builder.UseSetting("Billing:Plans:1:StorageGB", "500");
                builder.UseSetting("Billing:Plans:1:MonthlyPrice", "29");
                builder.UseSetting("Billing:Plans:1:ProviderPriceId", "price_starter");
                builder.UseSetting("Billing:Plans:2:Id", "business");
                builder.UseSetting("Billing:Plans:2:Name", "Business");
                builder.UseSetting("Billing:Plans:2:Cameras", "0");
                builder.UseSetting("Billing:Plans:2:StorageGB", "5000");
                builder.UseSetting("Billing:Plans:2:MonthlyPrice", "199");
                builder.UseSetting("Billing:Plans:2:ProviderPriceId", "price_business");
                builder.UseSetting("Billing:Stripe:SecretKey", "sk_test_key");
                builder.UseSetting("Billing:Stripe:WebhookSecret", WebhookSecret);
                builder.UseSetting("Billing:Stripe:ApiUrl", "http://stripe.test");
                builder.UseSetting("Billing:OperatorOrganizationId", TestSettings.OrganizationId);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IBillingProvider>();
                    services.AddSingleton<IBillingProvider>(sp => new StripeBilling(sp.GetRequiredService<BillingOptions>(), new HttpClient(_stripe)));
                });
                more(builder);
            });

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private Task<HttpResponseMessage> AddCameraAsync(string name) =>
            _owner.PostAsJsonAsync("/api/config/cameras", new { name, siteId = TestSettings.SiteId, rtspUrl = "rtsp://127.0.0.1:9/" + name, enabled = false });

        /// <summary>A webhook as Stripe sends it: the event, signed with the endpoint's secret.</summary>
        private async Task<HttpResponseMessage> WebhookAsync(string type, object data, string secret = WebhookSecret, DateTimeOffset? at = null)
        {
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["id"] = "evt_" + Guid.NewGuid().ToString("N"),
                ["object"] = "event",
                ["api_version"] = "2025-01-01",
                ["type"] = type,
                ["data"] = new { @object = data }
            });
            var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
            request.Headers.Add("Stripe-Signature", $"t={timestamp},v1={signature}");
            return await _factory.CreateClient().SendAsync(request);
        }

        private static object Subscription(string status, string price = "price_starter", string organizationId = TestSettings.OrganizationId) => new Dictionary<string, object>
        {
            ["id"] = "sub_1",
            ["object"] = "subscription",
            ["status"] = status,
            ["customer"] = "cus_1",
            ["metadata"] = new Dictionary<string, string> { ["organization_id"] = organizationId },
            ["items"] = new { @object = "list", data = new[] { new Dictionary<string, object> { ["id"] = "si_1", ["object"] = "subscription_item", ["current_period_end"] = 1_800_000_000L, ["price"] = new { id = price, @object = "price" } } } }
        };

        private async Task<JsonElement> BillingAsync() => await ReadAsync(await _owner.GetAsync("/api/billing"));

        /// <summary>The id of the offered version of a plan.</summary>
        private async Task<string> PlanIdAsync(string code) =>
            (await BillingAsync()).GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("code").GetString() == code).GetProperty("id").GetString();

        [Fact]
        public async Task AnOrganizationShouldTryAPlanThenPayForOne()
        {
            // On the trial: its plan, its limits, and every plan from the configuration.
            var billing = await BillingAsync();
            Assert.Equal(("Trialing", "trial", "EUR", true), (billing.GetProperty("state").GetString(), billing.GetProperty("plan").GetProperty("code").GetString(), billing.GetProperty("currency").GetString(), billing.GetProperty("canPay").GetBoolean()));
            Assert.Equal(new[] { "trial", "starter", "business" }, billing.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
            var starter = await PlanIdAsync("starter");
            Assert.Equal(29m, billing.GetProperty("plans")[1].GetProperty("monthlyPrice").GetDecimal());
            var trialEnds = billing.GetProperty("trialEndsAt").GetDateTimeOffset();
            Assert.InRange((trialEnds - DateTimeOffset.UtcNow).TotalDays, 13.9, 14.1);

            // Its cameras: two, then no more until a larger plan.
            Assert.Equal(HttpStatusCode.Created, (await AddCameraAsync("Door")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await AddCameraAsync("Yard")).StatusCode);
            var refused = await AddCameraAsync("Dock");
            Assert.Equal(HttpStatusCode.PaymentRequired, refused.StatusCode);
            Assert.Contains("includes 2 camera(s)", await refused.Content.ReadAsStringAsync());
            Assert.Equal(2, (await BillingAsync()).GetProperty("usage").GetProperty("cameras").GetInt32());

            // Checkout at Stripe for a plan, for this organization, back to the web app.
            var checkout = await _owner.PostAsJsonAsync("/api/billing/checkout", new { planId = starter, returnUrl = "http://localhost/settings/billing" });
            Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
            Assert.Equal("https://checkout.stripe.test/cs_1", (await ReadAsync(checkout)).GetProperty("url").GetString());
            var call = _stripe.Calls.Single(c => c.Path == "/v1/checkout/sessions");
            Assert.Equal(("sk_test_key", "subscription", "price_starter", TestSettings.OrganizationId, "http://localhost/settings/billing?checkout=done"),
                (call.Key, call.Form["mode"], call.Form["line_items[0][price]"], call.Form["client_reference_id"], call.Form["success_url"]));
            Assert.Equal((TestSettings.OrganizationId, starter), (call.Form["subscription_data[metadata][organization_id]"], call.Form["subscription_data[metadata][plan]"]));
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.PostAsJsonAsync("/api/billing/checkout", new { planId = starter, returnUrl = "https://evil.example/x" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.PostAsJsonAsync("/api/billing/checkout", new { planId = await PlanIdAsync("trial"), returnUrl = "http://localhost/settings/billing" })).StatusCode);

            // Webhooks only from Stripe, and only fresh ones.
            var session = new Dictionary<string, object> { ["id"] = "cs_1", ["object"] = "checkout.session", ["client_reference_id"] = TestSettings.OrganizationId, ["customer"] = "cus_1", ["subscription"] = "sub_1", ["metadata"] = new Dictionary<string, string> { ["plan"] = starter } };
            Assert.Equal(HttpStatusCode.BadRequest, (await WebhookAsync("checkout.session.completed", session, secret: "whsec_wrong")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await WebhookAsync("checkout.session.completed", session, at: DateTimeOffset.UtcNow.AddMinutes(-10))).StatusCode);
            Assert.Equal("Trialing", (await BillingAsync()).GetProperty("state").GetString());

            // Paid: the plan, and room for more cameras.
            Assert.Equal(HttpStatusCode.OK, (await WebhookAsync("checkout.session.completed", session)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await WebhookAsync("customer.subscription.updated", Subscription("active"))).StatusCode);
            billing = await BillingAsync();
            Assert.Equal(("Active", "starter", true), (billing.GetProperty("state").GetString(), billing.GetProperty("plan").GetProperty("code").GetString(), billing.GetProperty("canManage").GetBoolean()));
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), billing.GetProperty("currentPeriodEnd").GetDateTimeOffset());
            Assert.Equal(HttpStatusCode.Created, (await AddCameraAsync("Dock")).StatusCode);

            // A subscription already: changed in Stripe's portal, not a second checkout.
            Assert.Equal(HttpStatusCode.Conflict, (await _owner.PostAsJsonAsync("/api/billing/checkout", new { planId = await PlanIdAsync("business"), returnUrl = "http://localhost/settings/billing" })).StatusCode);
            var portal = await ReadAsync(await _owner.PostAsJsonAsync("/api/billing/portal", new { returnUrl = "http://localhost/settings/billing" }));
            Assert.Equal("https://billing.stripe.test/p/bps_1", portal.GetProperty("url").GetString());
            Assert.Equal("cus_1", _stripe.Calls.Single(c => c.Path == "/v1/billing_portal/sessions").Form["customer"]);

            // Upgraded there: the plan follows the price.
            await WebhookAsync("customer.subscription.updated", Subscription("active", price: "price_business"));
            Assert.Equal("business", (await BillingAsync()).GetProperty("plan").GetProperty("code").GetString());

            // A failed payment, paid after all, then canceled: no new cameras (the ones there keep recording).
            await WebhookAsync("invoice.payment_failed", new Dictionary<string, object> { ["id"] = "in_1", ["object"] = "invoice", ["customer"] = "cus_1", ["parent"] = new { subscription_details = new { subscription = "sub_1" } } });
            Assert.Equal("PastDue", (await BillingAsync()).GetProperty("state").GetString());
            await WebhookAsync("customer.subscription.updated", Subscription("active", price: "price_business"));
            Assert.Equal("Active", (await BillingAsync()).GetProperty("state").GetString());
            await WebhookAsync("customer.subscription.deleted", Subscription("canceled", price: "price_business"));
            Assert.Equal("Canceled", (await BillingAsync()).GetProperty("state").GetString());
            var ended = await AddCameraAsync("Gate");
            Assert.Equal(HttpStatusCode.PaymentRequired, ended.StatusCode);
            Assert.Contains("subscription has ended", await ended.Content.ReadAsStringAsync());

            // What the organization sees in its audit log.
            await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
            var actions = (await ReadAsync(await _owner.GetAsync("/api/audit?action=billing"))).EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
            Assert.Contains("billing.checkout", actions);
            Assert.Contains("billing.change", actions);
        }

        [Fact]
        public async Task TrialsShouldEndUnpaid()
        {
            Assert.Equal("Trialing", (await BillingAsync()).GetProperty("state").GetString());
            using (var db = _settings.Contexts.CreateDbContext())
            {
                var organization = db.Organizations.Single(o => o.Id == TestSettings.OrganizationId);
                organization.TrialEndsAt = DateTime.UtcNow.AddMinutes(-1);
                db.SaveChanges();
            }

            Assert.Equal(1, await _factory.Services.GetRequiredService<BillingService>().EndTrialsAsync());
            Assert.Equal("Canceled", (await BillingAsync()).GetProperty("state").GetString());
            Assert.Equal("Canceled", (await ReadAsync(await _owner.GetAsync("/api/auth/me"))).GetProperty("organization").GetProperty("subscription").GetProperty("state").GetString());
        }

        [Fact]
        public async Task BillingSettingsShouldBeChecked()
        {
            using (var broken = Billing(builder => builder.UseSetting("Billing:TrialPlan", "gold")))
            {
                Assert.Contains("Billing:TrialPlan", Assert.ThrowsAny<Exception>(() => broken.CreateClient()).ToString());
            }

            using (var broken = Billing(builder => builder.UseSetting("Billing:Plans:2:ProviderPriceId", "")))
            {
                Assert.Contains("ProviderPriceId", Assert.ThrowsAny<Exception>(() => broken.CreateClient()).ToString());
            }

            // Administrators see billing, only owners pay; viewers see nothing of it.
            async Task<HttpClient> MemberAsync(string userName, string role)
            {
                Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/users", new { userName, password = "Temporary-Pass-1", role })).StatusCode);
                var member = _factory.CreateClient();
                member.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (await TestSettings.SignInAsync(_factory.CreateClient(), userName, "Temporary-Pass-1")).AccessToken);
                var changed = await ReadAsync(await member.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Temporary-Pass-1", newPassword = "Own-Password-123" }));
                member.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", changed.GetProperty("accessToken").GetString());
                return member;
            }

            var admin = await MemberAsync("ada", "Admin");
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/billing")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/billing/checkout", new { planId = await PlanIdAsync("starter"), returnUrl = "http://localhost/settings/billing" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/billing/plans")).StatusCode);
            var viewer = await MemberAsync("vic", "Viewer");
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/billing")).StatusCode);
        }

        [Fact]
        public async Task PlansShouldBeKeptAsTheyWereSold()
        {
            async Task<List<JsonElement>> PlansAsync() => (await ReadAsync(await _owner.GetAsync("/api/billing/plans"))).EnumerateArray().ToList();
            object Terms(string name, int cameras, decimal price, string priceId) => new { name, cameras, storageGB = 500, monthlyPrice = price, providerPriceId = priceId, sortOrder = 20 };

            // Seeded from the configuration, with the organizations on each (this one is on the trial once it looks).
            await BillingAsync();
            var plans = await PlansAsync();
            Assert.Equal(new[] { "trial", "starter", "business" }, plans.Select(p => p.GetProperty("code").GetString()));
            Assert.Equal(1, plans[0].GetProperty("organizations").GetInt32());
            var starter = plans[1].GetProperty("id").GetString();

            // Nobody on it yet: changed in place.
            var edited = await ReadAsync(await _owner.PutAsJsonAsync($"/api/billing/plans/{starter}", Terms("Starter", 5, 25m, "price_starter")));
            Assert.Equal((starter, 1, 25m), (edited.GetProperty("id").GetString(), edited.GetProperty("version").GetInt32(), edited.GetProperty("monthlyPrice").GetDecimal()));

            // Sold to this organization...
            await WebhookAsync("checkout.session.completed", new Dictionary<string, object> { ["id"] = "cs_1", ["object"] = "checkout.session", ["client_reference_id"] = TestSettings.OrganizationId, ["customer"] = "cus_1", ["subscription"] = "sub_1", ["metadata"] = new Dictionary<string, string> { ["plan"] = starter } });
            Assert.Equal(starter, (await BillingAsync()).GetProperty("plan").GetProperty("id").GetString());

            // ...so a new price is a new version: new customers get it, this one keeps what it bought.
            var next = await ReadAsync(await _owner.PutAsJsonAsync($"/api/billing/plans/{starter}", Terms("Starter", 5, 35m, "price_starter_v2")));
            var version2 = next.GetProperty("id").GetString();
            Assert.NotEqual(starter, version2);
            Assert.Equal(("starter", 2, 35m, true), (next.GetProperty("code").GetString(), next.GetProperty("version").GetInt32(), next.GetProperty("monthlyPrice").GetDecimal(), next.GetProperty("active").GetBoolean()));
            var old = (await PlansAsync()).Single(p => p.GetProperty("id").GetString() == starter);
            Assert.Equal((false, version2, 1), (old.GetProperty("active").GetBoolean(), old.GetProperty("replacedById").GetString(), old.GetProperty("organizations").GetInt32()));

            var billing = await BillingAsync();
            Assert.Equal((starter, 25m, false), (billing.GetProperty("plan").GetProperty("id").GetString(), billing.GetProperty("plan").GetProperty("monthlyPrice").GetDecimal(), billing.GetProperty("plan").GetProperty("active").GetBoolean()));
            Assert.Contains(version2, billing.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("id").GetString()));
            Assert.DoesNotContain(starter, billing.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("id").GetString()));

            // The old version is only kept, not changed; its Stripe price still finds it.
            Assert.Equal(HttpStatusCode.Conflict, (await _owner.PutAsJsonAsync($"/api/billing/plans/{starter}", Terms("Starter", 9, 1m, "price_x"))).StatusCode);
            await WebhookAsync("customer.subscription.updated", Subscription("active", price: "price_starter"));
            Assert.Equal(starter, (await BillingAsync()).GetProperty("plan").GetProperty("id").GetString());

            // Retired: not offered; the trial cannot be. New plans, once per code.
            var business = (await PlansAsync()).Single(p => p.GetProperty("code").GetString() == "business").GetProperty("id").GetString();
            Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsync($"/api/billing/plans/{business}/retire", null)).StatusCode);
            Assert.DoesNotContain("business", (await BillingAsync()).GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
            Assert.Equal(HttpStatusCode.Conflict, (await _owner.PostAsync($"/api/billing/plans/{(await PlansAsync())[0].GetProperty("id").GetString()}/retire", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/billing/plans", new { code = "enterprise", name = "Enterprise", cameras = 0, storageGB = 0, monthlyPrice = 499, providerPriceId = "price_enterprise", sortOrder = 40 })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await _owner.PostAsJsonAsync("/api/billing/plans", new { code = "enterprise", name = "Again", monthlyPrice = 1, providerPriceId = "price_y" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.PostAsJsonAsync("/api/billing/plans", new { code = "Not A Code", name = "X" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _owner.PostAsJsonAsync("/api/billing/plans", new { code = "paid", name = "Paid", monthlyPrice = 10 })).StatusCode);

            // Every change in the operator's audit log, with what it was and what it became.
            await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
            var audit = (await ReadAsync(await _owner.GetAsync("/api/audit?action=billing"))).EnumerateArray().ToList();
            var versioned = audit.Single(e => e.GetProperty("action").GetString() == "billing.plan_version");
            var details = JsonDocument.Parse(versioned.GetProperty("details").ToString()).RootElement;
            Assert.Equal((25m, 35m, starter), (details.GetProperty("before").GetProperty("monthlyPrice").GetDecimal(), details.GetProperty("after").GetProperty("monthlyPrice").GetDecimal(), details.GetProperty("replaces").GetString()));
            Assert.Contains(audit, e => e.GetProperty("action").GetString() == "billing.plan_update");
            Assert.Contains(audit, e => e.GetProperty("action").GetString() == "billing.plan_retire");
            Assert.Contains(audit, e => e.GetProperty("action").GetString() == "billing.plan_create");
        }

        [Fact]
        public async Task OnlyTheOperatorShouldManagePlans()
        {
            using (var other = Billing(builder => builder.UseSetting("Billing:OperatorOrganizationId", Guid.NewGuid().ToString("D"))))
            using (var owner = TestSettings.Authenticate(other.CreateClient()))
            {
                Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/billing/plans")).StatusCode);
                Assert.False((await ReadAsync(await owner.GetAsync("/api/auth/me"))).GetProperty("organization").GetProperty("billingOperator").GetBoolean());
            }

            Assert.True((await ReadAsync(await _owner.GetAsync("/api/auth/me"))).GetProperty("organization").GetProperty("billingOperator").GetBoolean());
        }
    }
}
