// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Two-factor sign-in with an authenticator app: setting it up, signing in with a code or a recovery code, the
    /// lockout, an organization that requires it, and an administrator's reset. The clock is the test's, so each code
    /// (which works once) can be followed by the next.
    /// </summary>
    public sealed class TwoFactorApiTest : IDisposable
    {
        public TwoFactorApiTest()
        {
            _settings = new TestSettings();
            _storage = new TempDirectory();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(_clock);
                });
            });
        }

        public void Dispose()
        {
            _factory.Dispose();
            _settings.Dispose();
            _storage.Dispose();
        }

        private readonly TestSettings _settings;
        private readonly TempDirectory _storage;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly ManualTimeProvider _clock = new ManualTimeProvider();

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private HttpClient Bearer(string token)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        /// <summary>The app's code now, after moving the clock on a step (each code works once).</summary>
        private string NextCode(byte[] secret)
        {
            _clock.Advance(Totp.Step);
            return Totp.Code(secret, Totp.StepAt(_clock.GetUtcNow()));
        }

        private static byte[] FromBase32(string text)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var bytes = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var c in text)
            {
                buffer = (buffer << 5) | alphabet.IndexOf(c);
                bits += 5;
                if (bits >= 8)
                {
                    bytes.Add((byte)(buffer >> (bits - 8)));
                    bits -= 8;
                }
            }

            return bytes.ToArray();
        }

        private async Task<HttpResponseMessage> LoginAsync(string userName, string password) =>
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { userName, password });

        private async Task<HttpResponseMessage> CodeAsync(string challenge, string code) =>
            await _factory.CreateClient().PostAsJsonAsync("/api/auth/login/two-factor", new { challenge, code });

        private async Task<string> ChallengeAsync(string userName = TestSettings.AdminName, string password = TestSettings.AdminPassword)
        {
            var answer = await ReadAsync(await LoginAsync(userName, password));
            Assert.True(answer.GetProperty("twoFactorRequired").GetBoolean());
            Assert.False(answer.TryGetProperty("accessToken", out _));
            return answer.GetProperty("challenge").GetString();
        }

        /// <summary>Turns it on for a signed-in client; returns the secret and the recovery codes.</summary>
        private async Task<(byte[] Secret, List<string> RecoveryCodes)> TurnOnAsync(HttpClient client)
        {
            var setup = await ReadAsync(await client.PostAsync("/api/auth/two-factor/setup", null));
            var secret = FromBase32(setup.GetProperty("secret").GetString());
            Assert.StartsWith("otpauth://totp/Onvif.Open:", setup.GetProperty("uri").GetString());

            var enabled = await client.PostAsJsonAsync("/api/auth/two-factor/enable", new { code = NextCode(secret) });
            Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
            return (secret, (await ReadAsync(enabled)).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()).ToList());
        }

        [Fact]
        public void CodesShouldBeThoseOfRfc6238()
        {
            var secret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");
            Assert.Equal("287082", Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(59))));
            Assert.Equal("081804", Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(1111111109))));
            Assert.Equal("005924", Totp.Code(secret, Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(1234567890))));
            Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.Base32(secret));

            // A step either side is fine (phone clocks drift), further is not; a used step is not again.
            var now = DateTimeOffset.FromUnixTimeSeconds(1234567890);
            Assert.NotNull(Totp.Verify(secret, Totp.Code(secret, Totp.StepAt(now) - 1), now, 0));
            Assert.Null(Totp.Verify(secret, Totp.Code(secret, Totp.StepAt(now) - 2), now, 0));
            Assert.Null(Totp.Verify(secret, Totp.Code(secret, Totp.StepAt(now)), now, Totp.StepAt(now)));
            Assert.Equal(Totp.StepAt(now), Totp.Verify(secret, "005 924", now, 0));
        }

        [Fact]
        public async Task SigningInShouldTakeTheAppsCodeOnceItIsOn()
        {
            var admin = TestSettings.Authenticate(_factory.CreateClient());
            Assert.Equal("Off", (await ReadAsync(await admin.GetAsync("/api/auth/me"))).GetProperty("twoFactor").GetString());

            // A wrong code does not turn it on.
            var setup = await ReadAsync(await admin.PostAsync("/api/auth/two-factor/setup", null));
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/auth/two-factor/enable", new { code = "000000" })).StatusCode);
            Assert.Equal("Pending", (await ReadAsync(await admin.GetAsync("/api/auth/me"))).GetProperty("twoFactor").GetString());

            var (secret, recovery) = await TurnOnAsync(admin);
            Assert.Equal(10, recovery.Distinct().Count());
            Assert.All(recovery, c => Assert.Matches("^[a-z2-7]{4}-[a-z2-7]{4}-[a-z2-7]{4}$", c));
            var me = await ReadAsync(await admin.GetAsync("/api/auth/me"));
            Assert.Equal(("On", 10), (me.GetProperty("twoFactor").GetString(), me.GetProperty("recoveryCodesLeft").GetInt32()));
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/auth/two-factor/setup", null)).StatusCode);

            // The password alone gives no tokens, only a challenge for the code.
            var challenge = await ChallengeAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, (await CodeAsync(challenge, "123456")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await CodeAsync("forged", NextCode(secret))).StatusCode);
            var code = NextCode(secret);
            var signedIn = await CodeAsync(challenge, code);
            Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Bearer((await ReadAsync(signedIn)).GetProperty("accessToken").GetString()).GetAsync("/api/cameras")).StatusCode);

            // A code works once.
            Assert.Equal(HttpStatusCode.Unauthorized, (await CodeAsync(await ChallengeAsync(), code)).StatusCode);

            // A recovery code works once, in any case and with or without its dashes.
            Assert.Equal(HttpStatusCode.OK, (await CodeAsync(await ChallengeAsync(), recovery[0].ToUpperInvariant().Replace("-", ""))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await CodeAsync(await ChallengeAsync(), recovery[0])).StatusCode);
            Assert.Equal(9, (await ReadAsync(await admin.GetAsync("/api/auth/me"))).GetProperty("recoveryCodesLeft").GetInt32());

            // A challenge lasts five minutes.
            challenge = await ChallengeAsync();
            _clock.Advance(TimeSpan.FromMinutes(6));
            Assert.Equal(HttpStatusCode.Unauthorized, (await CodeAsync(challenge, Totp.Code(secret, Totp.StepAt(_clock.GetUtcNow())))).StatusCode);

            // New recovery codes replace the old ones.
            var renewed = await ReadAsync(await admin.PostAsJsonAsync("/api/auth/two-factor/recovery-codes", new { code = NextCode(secret) }));
            Assert.Equal(10, renewed.GetProperty("recoveryCodes").GetArrayLength());
            Assert.Equal(HttpStatusCode.Unauthorized, (await CodeAsync(await ChallengeAsync(), recovery[1])).StatusCode);

            // Turning it off takes the password and a code.
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = "wrong", code = NextCode(secret) })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = TestSettings.AdminPassword, code = NextCode(secret) })).StatusCode);
            Assert.True((await ReadAsync(await LoginAsync(TestSettings.AdminName, TestSettings.AdminPassword))).TryGetProperty("accessToken", out _));

            await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
            var actions = (await ReadAsync(await admin.GetAsync("/api/audit?action=auth"))).EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
            Assert.Contains("auth.two_factor_on", actions);
            Assert.Contains("auth.two_factor_off", actions);
            Assert.Contains("auth.recovery_codes", actions);
        }

        [Fact]
        public async Task WrongCodesShouldLockTheAccountEvenWithTheRightPassword()
        {
            var (secret, _) = await TurnOnAsync(TestSettings.Authenticate(_factory.CreateClient()));

            // The password again does not reset the count: 3 + 2 wrong codes lock it (5 by default).
            for (var round = 0; round < 2; round++)
            {
                var challenge = await ChallengeAsync();
                for (var attempt = 0; attempt < 3 - round; attempt++)
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, (await CodeAsync(challenge, "000000")).StatusCode);
                }
            }

            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(TestSettings.AdminName, TestSettings.AdminPassword)).StatusCode);
        }

        [Fact]
        public async Task AnOrganizationShouldBeAbleToRequireIt()
        {
            var owner = TestSettings.Authenticate(_factory.CreateClient());
            Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/users", new { userName = "dana", password = "Temporary-Pass-1", role = "Viewer" })).StatusCode);
            var dana = (await ReadAsync(await owner.GetAsync("/api/users"))).EnumerateArray().First(u => u.GetProperty("userName").GetString() == "dana").GetProperty("id").GetString();

            // The owner turns it on for themselves first.
            var refused = await owner.PutAsJsonAsync("/api/organization", new { requireTwoFactor = true });
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("your own account first", await refused.Content.ReadAsStringAsync());
            var (ownerSecret, _) = await TurnOnAsync(owner);
            var organization = await ReadAsync(await owner.PutAsJsonAsync("/api/organization", new { requireTwoFactor = true }));
            Assert.Equal(("Test organization", true), (organization.GetProperty("name").GetString(), organization.GetProperty("requireTwoFactor").GetBoolean()));

            // A member without it can only set it up (after their own password).
            var tokens = await TestSettings.SignInAsync(_factory.CreateClient(), "dana", "Temporary-Pass-1");
            var member = Bearer(tokens.AccessToken);
            Assert.Equal(HttpStatusCode.OK, (await member.PostAsJsonAsync("/api/auth/password", new { currentPassword = "Temporary-Pass-1", newPassword = "Dana-Own-Pass-1" })).StatusCode);
            tokens = await TestSettings.SignInAsync(_factory.CreateClient(), "dana", "Dana-Own-Pass-1");
            member = Bearer(tokens.AccessToken);
            Assert.True((await ReadAsync(await member.GetAsync("/api/auth/me"))).GetProperty("mustSetUpTwoFactor").GetBoolean());
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/cameras")).StatusCode);

            var (secret, _) = await TurnOnAsync(member);
            var refreshed = await ReadAsync(await _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken }));
            member = Bearer(refreshed.GetProperty("accessToken").GetString());
            Assert.False((await ReadAsync(await member.GetAsync("/api/auth/me"))).GetProperty("mustSetUpTwoFactor").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/cameras")).StatusCode);

            // Required: it stays on.
            Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/auth/two-factor/disable", new { password = "Dana-Own-Pass-1", code = NextCode(secret) })).StatusCode);

            // Lost phone: an administrator resets it, which ends the member's sessions.
            Assert.Equal("On", (await ReadAsync(await owner.GetAsync("/api/users"))).EnumerateArray().First(u => u.GetProperty("id").GetString() == dana).GetProperty("twoFactor").GetString());
            Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/users/{dana}/two-factor/reset", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken })).StatusCode);
            var again = await ReadAsync(await LoginAsync("dana", "Dana-Own-Pass-1"));
            Assert.True((await ReadAsync(await Bearer(again.GetProperty("accessToken").GetString()).GetAsync("/api/auth/me"))).GetProperty("mustSetUpTwoFactor").GetBoolean());

            // Its sessions ended with the reset; and only administrators reset anyone.
            Assert.Equal(HttpStatusCode.Unauthorized, (await member.GetAsync("/api/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Bearer(again.GetProperty("accessToken").GetString()).PostAsync($"/api/users/{dana}/two-factor/reset", null)).StatusCode);
            Assert.NotNull(ownerSecret);
        }
    }
}
