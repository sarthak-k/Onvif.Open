// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Single sign-on with an organization's identity provider (OpenID Connect), against a provider in memory: the trip
    /// there and back, the checks on its answer, linking and making accounts, and requiring it.
    /// </summary>
    public sealed class SingleSignOnTest : IDisposable
    {
        public SingleSignOnTest()
        {
            _settings = new TestSettings();
            _storage = new TempDirectory();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<SsoHttp>();
                    services.AddSingleton(new SsoHttp(_idp, requireHttps: false));
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

        private readonly TestSettings _settings;
        private readonly TempDirectory _storage;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _owner;
        private readonly FakeProvider _idp = new FakeProvider();

        /// <summary>An identity provider: discovery, keys, and a token endpoint for the codes the test hands out.</summary>
        private sealed class FakeProvider : HttpMessageHandler
        {
            public const string Issuer = "http://idp.test";
            public const string ClientId = "onvif-open";
            public const string ClientSecret = "provider-secret";

            public RSA Key { get; } = RSA.Create(2048);
            public RSA OtherKey { get; } = RSA.Create(2048);
            private readonly ConcurrentDictionary<string, (Dictionary<string, object> Claims, string Challenge, RSA Signer)> _codes = new();

            /// <summary>What the person did at the provider: signed in, so the provider has a code for the recorder.</summary>
            public string Authorize(Uri authorizeUrl, string subject, string email, bool verified = true, string nonce = null, RSA signer = null, string audience = ClientId)
            {
                var query = HttpUtility.ParseQueryString(authorizeUrl.Query);
                var code = Guid.NewGuid().ToString("N");
                _codes[code] = (new Dictionary<string, object>
                {
                    ["iss"] = Issuer,
                    ["aud"] = audience,
                    ["sub"] = subject,
                    ["email"] = email,
                    ["email_verified"] = verified,
                    ["nonce"] = nonce ?? query["nonce"]
                }, query["code_challenge"], signer ?? Key);
                return code;
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var path = request.RequestUri.AbsolutePath;
                if (path == "/.well-known/openid-configuration")
                {
                    return Json(new { issuer = Issuer, authorization_endpoint = Issuer + "/authorize", token_endpoint = Issuer + "/token", jwks_uri = Issuer + "/keys", id_token_signing_alg_values_supported = new[] { "RS256" } });
                }

                if (path == "/keys")
                {
                    var p = Key.ExportParameters(false);
                    return Json(new { keys = new[] { new { kty = "RSA", use = "sig", alg = "RS256", kid = "k1", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) } } });
                }

                if (path == "/token")
                {
                    var form = HttpUtility.ParseQueryString(await request.Content.ReadAsStringAsync(cancellationToken));
                    if (form["client_id"] != ClientId || form["client_secret"] != ClientSecret || !_codes.TryRemove(form["code"] ?? string.Empty, out var issued)
                        || Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"] ?? string.Empty))) != issued.Challenge
                        || !form["redirect_uri"].EndsWith("/api/auth/sso/callback", StringComparison.Ordinal))
                    {
                        return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };
                    }

                    var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
                    {
                        Claims = issued.Claims,
                        IssuedAt = DateTime.UtcNow,
                        NotBefore = DateTime.UtcNow,
                        Expires = DateTime.UtcNow.AddMinutes(5),
                        SigningCredentials = new SigningCredentials(new RsaSecurityKey(issued.Signer) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256)
                    });
                    return Json(new { access_token = "x", token_type = "Bearer", id_token = token });
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            private static HttpResponseMessage Json(object body) => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
        }

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private HttpClient Browser() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        private async Task<HttpResponseMessage> ConfigureAsync(object changes = null)
        {
            var settings = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["issuer"] = FakeProvider.Issuer,
                ["clientId"] = FakeProvider.ClientId,
                ["clientSecret"] = FakeProvider.ClientSecret,
                ["domains"] = new[] { "riverside.test" },
                ["autoCreate"] = true,
                ["defaultRole"] = "Operator",
                ["required"] = false
            };
            foreach (var property in changes?.GetType().GetProperties() ?? Array.Empty<System.Reflection.PropertyInfo>())
            {
                settings[property.Name] = property.GetValue(changes);
            }

            return await _owner.PutAsJsonAsync("/api/organization/sso", settings);
        }

        /// <summary>The browser's trip: start, the provider (as <paramref name="signIn"/> says), and back to the callback.</summary>
        private async Task<HttpResponseMessage> RoundTripAsync(string email, Func<Uri, string> signIn, bool keepCookie = true)
        {
            var browser = Browser();
            var start = await browser.GetAsync($"/api/auth/sso/start?email={Uri.EscapeDataString(email)}&returnTo={Uri.EscapeDataString("http://localhost")}");
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            var authorize = start.Headers.Location;
            Assert.StartsWith(FakeProvider.Issuer + "/authorize?", authorize.ToString());
            var cookie = start.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(SsoService.BindingCookie + "="));
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);

            var query = HttpUtility.ParseQueryString(authorize.Query);
            Assert.Equal(("code", FakeProvider.ClientId, "S256", email), (query["response_type"], query["client_id"], query["code_challenge_method"], query["login_hint"]));
            using (var callback = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/sso/callback?code={signIn(authorize)}&state={Uri.EscapeDataString(query["state"])}"))
            {
                if (keepCookie)
                {
                    callback.Headers.Add("Cookie", cookie.Split(';')[0]);
                }

                return await browser.SendAsync(callback);
            }
        }

        private static string HandoffOf(HttpResponseMessage back)
        {
            Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
            Assert.StartsWith("http://localhost/sso#code=", back.Headers.Location.ToString());
            return Uri.UnescapeDataString(back.Headers.Location.Fragment.Substring("#code=".Length));
        }

        private static string ErrorOf(HttpResponseMessage back)
        {
            Assert.Equal(HttpStatusCode.Redirect, back.StatusCode);
            Assert.StartsWith("http://localhost/login?sso_error=", back.Headers.Location.ToString());
            return HttpUtility.ParseQueryString(back.Headers.Location.Query)["sso_error"];
        }

        private async Task<HttpClient> SignedInAsync(string handoff)
        {
            var exchanged = await _factory.CreateClient().PostAsJsonAsync("/api/auth/sso/exchange", new { code = handoff });
            Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await ReadAsync(exchanged)).GetProperty("accessToken").GetString());
            return client;
        }

        [Fact]
        public async Task PeopleShouldSignInWithTheirOrganizationsProvider()
        {
            // Set up by the owner; the secret is never shown again.
            Assert.Equal(HttpStatusCode.OK, (await ConfigureAsync()).StatusCode);
            var settings = await ReadAsync(await _owner.GetAsync("/api/organization/sso"));
            Assert.True(settings.GetProperty("hasClientSecret").GetBoolean());
            Assert.DoesNotContain(FakeProvider.ClientSecret, settings.ToString());

            // Found by the email's domain.
            Assert.True((await ReadAsync(await Browser().GetAsync("/api/auth/sso?email=ana@Riverside.test"))).GetProperty("available").GetBoolean());
            Assert.False((await ReadAsync(await Browser().GetAsync("/api/auth/sso?email=ana@elsewhere.test"))).GetProperty("available").GetBoolean());

            // The first sign-in makes the account, with the default role.
            var handoff = HandoffOf(await RoundTripAsync("ana@riverside.test", url => _idp.Authorize(url, "sub-ana", "ana@riverside.test")));
            var ana = await SignedInAsync(handoff);
            var me = await ReadAsync(await ana.GetAsync("/api/auth/me"));
            Assert.Equal(("ana@riverside.test", "Operator"), (me.GetProperty("userName").GetString(), me.GetProperty("role").GetString()));
            Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/cameras")).StatusCode);

            // The one-time code works once.
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/api/auth/sso/exchange", new { code = handoff })).StatusCode);

            // Next time by the provider's subject, whatever the email says now.
            handoff = HandoffOf(await RoundTripAsync("ana@riverside.test", url => _idp.Authorize(url, "sub-ana", "ana.new@riverside.test")));
            Assert.Equal("ana@riverside.test", (await ReadAsync(await (await SignedInAsync(handoff)).GetAsync("/api/auth/me"))).GetProperty("userName").GetString());
            Assert.Single((await ReadAsync(await _owner.GetAsync("/api/users"))).EnumerateArray(), u => u.GetProperty("userName").GetString().StartsWith("ana"));

            await _factory.Services.GetRequiredService<Onvif.Open.Recorder.Audit.AuditLog>().FlushAsync();
            var audit = (await ReadAsync(await _owner.GetAsync("/api/audit?action=auth.signin"))).EnumerateArray().ToList();
            Assert.Contains(audit, e => e.GetProperty("userName").GetString() == "ana@riverside.test");
        }

        [Fact]
        public async Task ItShouldLinkExistingMembersByTheirVerifiedEmailOnly()
        {
            Assert.Equal(HttpStatusCode.OK, (await ConfigureAsync(new { autoCreate = false })).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/users", new { userName = "ben@riverside.test", password = "Temporary-Pass-1", role = "Viewer" })).StatusCode);

            // Not verified: no link. Unknown and no account making: no account.
            Assert.Contains("did not confirm your email", ErrorOf(await RoundTripAsync("ben@riverside.test", url => _idp.Authorize(url, "sub-ben", "ben@riverside.test", verified: false))));
            Assert.Contains("ask an administrator", ErrorOf(await RoundTripAsync("cleo@riverside.test", url => _idp.Authorize(url, "sub-cleo", "cleo@riverside.test"))));

            var ben = await SignedInAsync(HandoffOf(await RoundTripAsync("ben@riverside.test", url => _idp.Authorize(url, "sub-ben", "ben@riverside.test"))));
            Assert.Equal("Viewer", (await ReadAsync(await ben.GetAsync("/api/auth/me"))).GetProperty("role").GetString());
        }

        [Fact]
        public async Task TheProvidersAnswerShouldBeChecked()
        {
            Assert.Equal(HttpStatusCode.OK, (await ConfigureAsync()).StatusCode);

            // Another browser's answer (no cookie), a forged state.
            Assert.Equal(HttpStatusCode.BadRequest, (await RoundTripAsync("ana@riverside.test", url => _idp.Authorize(url, "sub-ana", "ana@riverside.test"), keepCookie: false)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await Browser().GetAsync("/api/auth/sso/callback?code=x&state=forged")).StatusCode);

            // Signed with a key that is not the provider's, for another client, for another sign-in.
            foreach (var (signIn, why) in new (Func<Uri, string>, string)[]
            {
                (url => _idp.Authorize(url, "sub-ana", "ana@riverside.test", signer: _idp.OtherKey), "key"),
                (url => _idp.Authorize(url, "sub-ana", "ana@riverside.test", audience: "someone-else"), "audience"),
                (url => _idp.Authorize(url, "sub-ana", "ana@riverside.test", nonce: "replayed"), "nonce"),
                (url => "made-up-code", "code")
            })
            {
                Assert.Contains("could not be used", ErrorOf(await RoundTripAsync("ana@riverside.test", signIn)));
            }

            // The provider's own refusal.
            var browser = Browser();
            var start = await browser.GetAsync("/api/auth/sso/start?email=ana@riverside.test&returnTo=http%3A%2F%2Flocalhost");
            var state = HttpUtility.ParseQueryString(start.Headers.Location.Query)["state"];
            using (var denied = new HttpRequestMessage(HttpMethod.Get, $"/api/auth/sso/callback?error=access_denied&state={Uri.EscapeDataString(state)}"))
            {
                denied.Headers.Add("Cookie", start.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
                Assert.Contains("access_denied", ErrorOf(await browser.SendAsync(denied)));
            }

            // Only back to the web app, never elsewhere.
            Assert.Equal(HttpStatusCode.BadRequest, (await Browser().GetAsync("/api/auth/sso/start?email=ana@riverside.test&returnTo=https%3A%2F%2Fevil.example")).StatusCode);
            Assert.DoesNotContain((await ReadAsync(await _owner.GetAsync("/api/users"))).EnumerateArray(), u => u.GetProperty("userName").GetString() == "ana@riverside.test");
        }

        [Fact]
        public async Task SettingsShouldBeChecked()
        {
            foreach (var bad in new object[]
            {
                new { issuer = "not a url" },
                new { clientId = "" },
                new { domains = Array.Empty<string>() },
                new { domains = new[] { "not a domain" } },
                new { defaultRole = "Owner" },
                new { enabled = false, required = true }
            })
            {
                Assert.Equal(HttpStatusCode.BadRequest, (await ConfigureAsync(bad)).StatusCode);
            }

            // A domain belongs to one organization.
            using (var db = _settings.Contexts.CreateDbContext())
            {
                db.SsoDomains.Add(new Onvif.Open.Recorder.Store.SsoDomainRecord { Domain = "taken.test", OrganizationId = Guid.NewGuid().ToString("D") });
                db.SaveChanges();
            }

            var taken = await ConfigureAsync(new { domains = new[] { "taken.test" } });
            Assert.Contains("Another organization", await taken.Content.ReadAsStringAsync());

            // Owners only.
            Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/users", new { userName = "dana", password = "Temporary-Pass-1", role = "Admin" })).StatusCode);
            var tokens = await TestSettings.SignInAsync(_factory.CreateClient(), "dana", "Temporary-Pass-1");
            var admin = _factory.CreateClient();
            admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync("/api/organization/sso", new { enabled = false })).StatusCode);
        }

        [Fact]
        public async Task RequiredSingleSignOnShouldLeaveOnlyOwnersTheirPassword()
        {
            Assert.Equal(HttpStatusCode.OK, (await ConfigureAsync()).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await _owner.PostAsJsonAsync("/api/users", new { userName = "eve@riverside.test", password = "Temporary-Pass-1", role = "Viewer" })).StatusCode);

            // Two-factor sign-in required by the organization is the provider's business for these sign-ins.
            await TurnOnTwoFactorForOwnerAndRequireItAsync();
            Assert.Equal(HttpStatusCode.OK, (await ConfigureAsync(new { required = true })).StatusCode);

            var refused = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { userName = "eve@riverside.test", password = "Temporary-Pass-1" });
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Contains("single sign-on", await refused.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { userName = TestSettings.AdminName, password = TestSettings.AdminPassword })).StatusCode);

            var handoff = HandoffOf(await RoundTripAsync("eve@riverside.test", url => _idp.Authorize(url, "sub-eve", "eve@riverside.test")));
            var exchanged = await ReadAsync(await _factory.CreateClient().PostAsJsonAsync("/api/auth/sso/exchange", new { code = handoff }));
            var eve = _factory.CreateClient();
            eve.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", exchanged.GetProperty("accessToken").GetString());
            Assert.False((await ReadAsync(await eve.GetAsync("/api/auth/me"))).GetProperty("mustSetUpTwoFactor").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await eve.GetAsync("/api/cameras")).StatusCode);

            // ... and stays so when the session is renewed.
            var renewed = await ReadAsync(await _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = exchanged.GetProperty("refreshToken").GetString() }));
            eve.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renewed.GetProperty("accessToken").GetString());
            Assert.Equal(HttpStatusCode.OK, (await eve.GetAsync("/api/cameras")).StatusCode);
        }

        private async Task TurnOnTwoFactorForOwnerAndRequireItAsync()
        {
            var setup = await ReadAsync(await _owner.PostAsync("/api/auth/two-factor/setup", null));
            var secret = Base32Decode(setup.GetProperty("secret").GetString());
            Assert.Equal(HttpStatusCode.OK, (await _owner.PostAsJsonAsync("/api/auth/two-factor/enable", new { code = Totp.Code(secret, Totp.StepAt(DateTimeOffset.UtcNow)) })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await _owner.PutAsJsonAsync("/api/organization", new { requireTwoFactor = true })).StatusCode);
        }

        private static byte[] Base32Decode(string text)
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
    }
}
