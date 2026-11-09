// Sarthak Killedar
// 2026-11-09

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// A gateway on another machine pairs with this recorder (the control plane) with a 6-digit code, then reports its
    /// health; a removed gateway pairs again.
    /// </summary>
    public sealed class GatewayPairingTest : IDisposable
    {
        public GatewayPairingTest()
        {
            _storage = new TempDirectory();
            _settings = new TestSettings();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
            });
            _admin = TestSettings.Authenticate(_factory.CreateClient());
        }

        private sealed class MemoryCredentials : IControlPlaneCredentials
        {
            public ControlPlaneIdentity Stored { get; private set; }
            public ControlPlaneIdentity Load() => Stored;
            public void Save(ControlPlaneIdentity identity) => Stored = identity;
            public void Clear() => Stored = null;
        }

        private readonly TempDirectory _storage;
        private readonly TestSettings _settings;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _admin;

        private ControlPlaneClient Gateway(MemoryCredentials credentials) =>
            new ControlPlaneClient(_factory.CreateClient(), credentials, () => new GatewayHealth { Cameras = 2, Recording = 2, FreeBytes = 1_000_000 }, "Warehouse box", "Local", NullLogger<ControlPlaneClient>.Instance);

        private async Task<JsonElement> GatewaysAsync() => JsonDocument.Parse(await _admin.GetStringAsync("/api/gateways")).RootElement;

        [Fact]
        public async Task AGatewayShouldPairWithItsCodeAndReport()
        {
            var credentials = new MemoryCredentials();
            var gateway = Gateway(credentials);

            // The gateway asks for a code and waits.
            Assert.Equal(ControlPlaneClient.PollInterval, await gateway.StepAsync());
            var code = gateway.PairingCode;
            Assert.Matches("^[0-9]{6}$", code);
            Assert.Equal(ControlPlaneClient.PollInterval, await gateway.StepAsync());
            Assert.False(gateway.IsPaired);

            // A wrong code pairs nothing; the right one, typed with a space, does.
            var wrong = (int.Parse(code) + 1) % 1_000_000;
            Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/gateways/pair", new { code = wrong.ToString("D6") })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/gateways/pair", new { code = "12" })).StatusCode);
            var paired = await _admin.PostAsJsonAsync("/api/gateways/pair", new { code = $"{code.Substring(0, 3)} {code.Substring(3)}" });
            Assert.Equal(HttpStatusCode.Created, paired.StatusCode);
            var created = JsonDocument.Parse(await paired.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(("Warehouse box", false, "Waiting"), (created.GetProperty("name").GetString(), created.GetProperty("embedded").GetBoolean(), created.GetProperty("status").GetString()));
            Assert.Equal(HttpStatusCode.BadRequest, (await _admin.PostAsJsonAsync("/api/gateways/pair", new { code })).StatusCode);

            // The gateway collects its token once, then reports.
            Assert.Equal(TimeSpan.Zero, await gateway.StepAsync());
            Assert.True(gateway.IsPaired);
            Assert.Equal(created.GetProperty("id").GetString(), credentials.Stored.GatewayId);
            Assert.Equal(ControlPlaneClient.HeartbeatInterval, await gateway.StepAsync());

            var reported = (await GatewaysAsync()).EnumerateArray().Single(g => !g.GetProperty("embedded").GetBoolean());
            Assert.Equal("Online", reported.GetProperty("status").GetString());
            Assert.Equal(2, reported.GetProperty("health").GetProperty("recording").GetInt32());
            Assert.False(string.IsNullOrEmpty(reported.GetProperty("version").GetString()));

            // Nobody else reports as that gateway.
            using (var forged = new HttpRequestMessage(HttpMethod.Post, "/api/gateways/heartbeat") { Content = JsonContent.Create(new { version = "x" }) })
            {
                forged.Headers.Add("X-Gateway-Token", "not-the-token");
                Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().SendAsync(forged)).StatusCode);
            }

            // This server's own gateway stays; the paired one can be removed, and then pairs again.
            Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync($"/api/gateways/{TestSettings.GatewayId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/gateways/{credentials.Stored.GatewayId}")).StatusCode);
            Assert.Equal(TimeSpan.Zero, await gateway.StepAsync());
            Assert.Null(credentials.Stored);
            await gateway.StepAsync();
            Assert.Matches("^[0-9]{6}$", gateway.PairingCode);

            await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
            var audited = JsonDocument.Parse(await _admin.GetStringAsync("/api/audit?action=gateway")).RootElement.EnumerateArray().Select(e => e.GetProperty("action").GetString());
            Assert.Equal(new[] { "gateway.remove", "gateway.pair" }, audited);
        }

        [Fact]
        public async Task PairingsShouldNeedTheirSecretAndAnAdministrator()
        {
            using (var anonymous = _factory.CreateClient())
            {
                var started = await anonymous.PostAsJsonAsync("/api/gateways/pairing", new { name = "Box" });
                Assert.Equal(HttpStatusCode.Created, started.StatusCode);
                var ticket = JsonDocument.Parse(await started.Content.ReadAsStringAsync()).RootElement;

                // Without its secret, a pairing is unknown.
                Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/gateways/pairing/{ticket.GetProperty("id").GetString()}")).StatusCode);

                // Entering a code needs a signed-in administrator.
                Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/gateways/pair", new { code = ticket.GetProperty("code").GetString() })).StatusCode);
            }
        }

        [Fact]
        public void TheTokenShouldBeStoredEncrypted()
        {
            var path = Path.Combine(_storage.Path, "control-plane.json");
            var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_storage.Path, "keys")));
            var store = new FileControlPlaneCredentials(path, protection);

            Assert.Null(store.Load());
            store.Save(new ControlPlaneIdentity("gw-1", "very-secret-token"));
            Assert.DoesNotContain("very-secret-token", File.ReadAllText(path));
            Assert.Equal(new ControlPlaneIdentity("gw-1", "very-secret-token"), new FileControlPlaneCredentials(path, protection).Load());
            store.Clear();
            Assert.Null(store.Load());
        }

        public void Dispose()
        {
            _admin.Dispose();
            _factory.Dispose();
            _settings.Dispose();
            _storage.Dispose();
        }
    }
}
