// Sarthak Killedar
// 2026-11-11

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Web;
using System.Xml;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Single sign-on with a SAML 2.0 provider: the request, and the posted response checked strictly (signature over the
    /// element that is read, issuer, audience, recipient, request, time), including the classic attacks on it.
    /// </summary>
    public sealed class SamlSignOnTest : IDisposable
    {
        public SamlSignOnTest()
        {
            _settings = new TestSettings();
            _storage = new TempDirectory();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                _settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", _storage.Path);
            });
            _owner = TestSettings.Authenticate(_factory.CreateClient());
            _key = RSA.Create(2048);
            _certificate = new CertificateRequest("CN=idp.saml.test", _key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        }

        public void Dispose()
        {
            _owner.Dispose();
            _factory.Dispose();
            _settings.Dispose();
            _storage.Dispose();
            _certificate.Dispose();
            _key.Dispose();
        }

        private const string Provider = "https://idp.saml.test";
        private const string Acs = "http://localhost/api/auth/sso/saml/acs";
        private const string Me = "http://localhost/api/auth/sso/saml/metadata";

        private readonly TestSettings _settings;
        private readonly TempDirectory _storage;
        private readonly WebApplicationFactory<Program> _factory;
        private readonly HttpClient _owner;
        private readonly RSA _key;
        private readonly X509Certificate2 _certificate;

        private sealed record Trip(HttpClient Browser, string Cookie, string RequestId, string RelayState);

        private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        private async Task ConfigureAsync(bool autoCreate = true)
        {
            var saved = await _owner.PutAsJsonAsync("/api/organization/sso", new
            {
                enabled = true,
                protocol = "Saml",
                samlEntityId = Provider,
                samlSsoUrl = Provider + "/sso",
                samlCertificate = _certificate.ExportCertificatePem(),
                domains = new[] { "riverside.test" },
                autoCreate,
                defaultRole = "Viewer"
            });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var settings = await ReadAsync(saved);
            Assert.Equal(("Saml", "CN=idp.saml.test"), (settings.GetProperty("protocol").GetString(), settings.GetProperty("samlCertificateSubject").GetString()));
        }

        /// <summary>The browser sent to the provider: the request it carries, and the cookie it keeps.</summary>
        private async Task<Trip> StartAsync(string email)
        {
            var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
            var start = await browser.GetAsync($"/api/auth/sso/start?email={Uri.EscapeDataString(email)}&returnTo={Uri.EscapeDataString("http://localhost")}");
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            Assert.StartsWith(Provider + "/sso?SAMLRequest=", start.Headers.Location.ToString());
            var cookie = start.Headers.GetValues("Set-Cookie").Single();
            Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);

            var query = HttpUtility.ParseQueryString(start.Headers.Location.Query);
            using var inflated = new DeflateStream(new MemoryStream(Convert.FromBase64String(query["SAMLRequest"])), CompressionMode.Decompress);
            var request = new XmlDocument();
            request.Load(inflated);
            Assert.Equal((Acs, email), (request.DocumentElement.GetAttribute("AssertionConsumerServiceURL"), request.DocumentElement.InnerText.Replace(Me, string.Empty)));
            return new Trip(browser, cookie.Split(';')[0], request.DocumentElement.GetAttribute("ID"), query["RelayState"]);
        }

        private async Task<HttpResponseMessage> PostAsync(Trip trip, string response, bool cookie = true)
        {
            using var post = new HttpRequestMessage(HttpMethod.Post, "/api/auth/sso/saml/acs")
            {
                Content = new FormUrlEncodedContent(new[] { KeyValuePair.Create("SAMLResponse", response), KeyValuePair.Create("RelayState", trip.RelayState) })
            };
            if (cookie)
            {
                post.Headers.Add("Cookie", trip.Cookie);
            }

            return await trip.Browser.SendAsync(post);
        }

        /// <summary>What a provider posts back, signed as asked (or tampered with afterwards).</summary>
        private string Response(string inResponseTo, string nameId, string email = null, string issuer = Provider, string audience = Me, string recipient = Acs,
            TimeSpan? validFor = null, bool signAssertion = true, bool signResponse = false, RSA signer = null, string signatureMethod = SignedXml.XmlDsigRSASHA256Url,
            Action<XmlDocument> afterSigning = null, string status = "urn:oasis:names:tc:SAML:2.0:status:Success")
        {
            var now = DateTime.UtcNow;
            var until = now + (validFor ?? TimeSpan.FromMinutes(5));
            string Time(DateTime t) => t.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var responseId = "_r" + Guid.NewGuid().ToString("N");
            var assertionId = "_a" + Guid.NewGuid().ToString("N");
            var xml = $@"<samlp:Response xmlns:samlp=""urn:oasis:names:tc:SAML:2.0:protocol"" xmlns:saml=""urn:oasis:names:tc:SAML:2.0:assertion"" ID=""{responseId}"" Version=""2.0"" IssueInstant=""{Time(now)}"" Destination=""{recipient}"" InResponseTo=""{inResponseTo}"">"
                + $@"<saml:Issuer>{issuer}</saml:Issuer><samlp:Status><samlp:StatusCode Value=""{status}""/></samlp:Status>"
                + $@"<saml:Assertion ID=""{assertionId}"" Version=""2.0"" IssueInstant=""{Time(now)}""><saml:Issuer>{issuer}</saml:Issuer>"
                + $@"<saml:Subject><saml:NameID Format=""urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress"">{nameId}</saml:NameID>"
                + $@"<saml:SubjectConfirmation Method=""urn:oasis:names:tc:SAML:2.0:cm:bearer""><saml:SubjectConfirmationData NotOnOrAfter=""{Time(until)}"" Recipient=""{recipient}"" InResponseTo=""{inResponseTo}""/></saml:SubjectConfirmation></saml:Subject>"
                + $@"<saml:Conditions NotBefore=""{Time(now.AddMinutes(-1))}"" NotOnOrAfter=""{Time(until)}""><saml:AudienceRestriction><saml:Audience>{audience}</saml:Audience></saml:AudienceRestriction></saml:Conditions>"
                + (email == null ? string.Empty : $@"<saml:AttributeStatement><saml:Attribute Name=""http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress""><saml:AttributeValue>{email}</saml:AttributeValue></saml:Attribute></saml:AttributeStatement>")
                + "</saml:Assertion></samlp:Response>";

            var document = new XmlDocument { PreserveWhitespace = true };
            document.LoadXml(xml);
            var assertion = (XmlElement)document.GetElementsByTagName("Assertion", "urn:oasis:names:tc:SAML:2.0:assertion")[0];
            if (signAssertion)
            {
                Sign(assertion, assertionId, signer ?? _key, signatureMethod);
            }

            if (signResponse)
            {
                Sign(document.DocumentElement, responseId, signer ?? _key, signatureMethod);
            }

            afterSigning?.Invoke(document);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(document.OuterXml));
        }

        private static void Sign(XmlElement element, string id, RSA key, string signatureMethod)
        {
            var signed = new SignedXml(element) { SigningKey = key };
            signed.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
            signed.SignedInfo.SignatureMethod = signatureMethod;
            var reference = new Reference("#" + id) { DigestMethod = signatureMethod == SignedXml.XmlDsigRSASHA1Url ? SignedXml.XmlDsigSHA1Url : SignedXml.XmlDsigSHA256Url };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigExcC14NTransform());
            signed.AddReference(reference);
            signed.ComputeSignature();
            element.InsertAfter(element.OwnerDocument.ImportNode(signed.GetXml(), true), element.FirstChild);
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

        [Fact]
        public async Task PeopleShouldSignInWithTheirSamlProvider()
        {
            await ConfigureAsync();

            // This recorder's metadata, for the provider's setup.
            var metadata = await _factory.CreateClient().GetStringAsync("/api/auth/sso/saml/metadata");
            Assert.Contains($"entityID=\"{Me}\"", metadata);
            Assert.Contains($"Location=\"{Acs}\"", metadata);

            var trip = await StartAsync("ana@riverside.test");
            var handoff = HandoffOf(await PostAsync(trip, Response(trip.RequestId, "ana@riverside.test")));
            var exchanged = await ReadAsync(await _factory.CreateClient().PostAsJsonAsync("/api/auth/sso/exchange", new { code = handoff }));
            var ana = _factory.CreateClient();
            ana.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", exchanged.GetProperty("accessToken").GetString());
            var me = await ReadAsync(await ana.GetAsync("/api/auth/me"));
            Assert.Equal(("ana@riverside.test", "Viewer", true), (me.GetProperty("userName").GetString(), me.GetProperty("role").GetString(), me.GetProperty("singleSignOn").GetBoolean()));

            // A whole response signed instead of its assertion; the email from an attribute; the same account again.
            trip = await StartAsync("ana@riverside.test");
            HandoffOf(await PostAsync(trip, Response(trip.RequestId, "ana@riverside.test", "ana@riverside.test", signAssertion: false, signResponse: true)));

            // A response answers its own request only once.
            var replayed = Response(trip.RequestId, "ana@riverside.test");
            var next = await StartAsync("ana@riverside.test");
            Assert.Contains("does not answer this sign-in", ErrorOf(await PostAsync(next, replayed)));
            Assert.Single((await ReadAsync(await _owner.GetAsync("/api/users"))).EnumerateArray(), u => u.GetProperty("userName").GetString() == "ana@riverside.test");
        }

        [Fact]
        public async Task ForgedOrStrayResponsesShouldBeRefused()
        {
            await ConfigureAsync();
            using var otherKey = RSA.Create(2048);
            var cases = new (Func<string, string> Make, string Why)[]
            {
                (id => Response(id, "ana@riverside.test", signAssertion: false), "not signed"),
                (id => Response(id, "ana@riverside.test", signer: otherKey), "not signed"),
                (id => Response(id, "ana@riverside.test", signatureMethod: SignedXml.XmlDsigRSASHA1Url), "not signed"),
                (id => Response(id, "ana@riverside.test", afterSigning: d => d.GetElementsByTagName("NameID", "urn:oasis:names:tc:SAML:2.0:assertion")[0].InnerText = "owner@riverside.test"), "not signed"),
                (id => Response(id, "ana@riverside.test", afterSigning: d =>
                {
                    // Signature wrapping: a second, unsigned assertion next to the signed one.
                    var assertion = d.GetElementsByTagName("Assertion", "urn:oasis:names:tc:SAML:2.0:assertion")[0];
                    var evil = (XmlElement)assertion.CloneNode(true);
                    evil.SetAttribute("ID", "_evil");
                    d.DocumentElement.InsertBefore(evil, assertion);
                }), "exactly one assertion"),
                (id => Response(id, "ana@riverside.test", issuer: "https://other-idp.test"), "another provider"),
                (id => Response(id, "ana@riverside.test", audience: "https://another-app.test"), "not for this recorder"),
                (id => Response(id, "ana@riverside.test", recipient: "https://another-app.test/acs"), "another address"),
                (id => Response(id, "ana@riverside.test", validFor: TimeSpan.FromMinutes(-10)), "not now"),
                (id => Response("_another-request", "ana@riverside.test"), "does not answer"),
                (id => Response(id, "ana@riverside.test", status: "urn:oasis:names:tc:SAML:2.0:status:Responder"), "did not sign you in"),
                (id => "not SAML at all", "not SAML")
            };

            foreach (var (make, why) in cases)
            {
                var trip = await StartAsync("ana@riverside.test");
                Assert.Contains(why, ErrorOf(await PostAsync(trip, make(trip.RequestId))));
            }

            // Another browser's response (no cookie), or no state at all.
            var stray = await StartAsync("ana@riverside.test");
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(stray, Response(stray.RequestId, "ana@riverside.test"), cookie: false)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await _factory.CreateClient().PostAsync("/api/auth/sso/saml/acs", new FormUrlEncodedContent(new[] { KeyValuePair.Create("SAMLResponse", "x") }))).StatusCode);

            Assert.DoesNotContain((await ReadAsync(await _owner.GetAsync("/api/users"))).EnumerateArray(), u => u.GetProperty("userName").GetString().EndsWith("@riverside.test"));
        }

        [Fact]
        public async Task OnlyEmailsOfTheOrganizationsDomainsShouldBeLinkedOrMade()
        {
            await ConfigureAsync();

            // The provider vouches only for the organization's own domains.
            var trip = await StartAsync("ana@riverside.test");
            Assert.Contains("did not confirm your email", ErrorOf(await PostAsync(trip, Response(trip.RequestId, "someone@elsewhere.test"))));

            // Settings are checked for SAML too.
            var bad = await _owner.PutAsJsonAsync("/api/organization/sso", new { enabled = true, protocol = "Saml", samlEntityId = Provider, samlSsoUrl = "ftp://x", samlCertificate = "not a certificate", domains = new[] { "riverside.test" } });
            var errors = await bad.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            Assert.Contains("sign-in address must be https", errors);
            Assert.Contains("not a certificate", errors);
        }
    }
}
