// Sarthak Killedar
// 2026-11-11

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: single sign-on (OpenID Connect). Owners set up their organization's identity provider; people of its
    /// email domains sign in there: the browser goes to <c>/api/auth/sso/start</c>, comes back to the callback, then to the
    /// web app's <c>/sso</c> with a one-time code it exchanges for tokens.
    /// </summary>
    internal static class SsoEndpoints
    {
        public sealed class ExchangeRequest
        {
            public string Code { get; set; }
        }

        public const string CallbackPath = "/api/auth/sso/callback";
        public const string SamlMetadataPath = "/api/auth/sso/saml/metadata";
        public const string SamlAcsPath = "/api/auth/sso/saml/acs";

        public static IEndpointRouteBuilder MapSsoApi(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/organization/sso", async (ClaimsPrincipal user, SsoService sso, CancellationToken ct) =>
                ToDto(await sso.GetAsync(user.OrganizationId(), ct)))
                .RequireAuthorization(Policies.OrganizationAdmin);

            app.MapPut("/api/organization/sso", async (SsoSettings request, ClaimsPrincipal user, SsoService sso, CancellationToken ct) =>
                TenancyEndpoints.ToResult(await sso.SaveAsync(user.OrganizationId(), request, ct), ToDto))
                .RequireAuthorization(Policies.Owner)
                .Audit(AuditActions.OrganizationSso, AuditTargets.Organization, details: context => context.GetArgument<SsoSettings>(0) is SsoSettings s
                    ? new { enabled = s.Enabled, protocol = s.Protocol, samlEntityId = s.SamlEntityId, issuer = s.Issuer, domains = s.Domains, autoCreate = s.AutoCreate, defaultRole = s.DefaultRole, required = s.Required, secretChanged = s.ClientSecret != null }
                    : null);

            var sso = app.MapGroup("/api/auth/sso").AllowAnonymous().RequireRateLimiting(SecurityConfiguration.SignInLimit);

            // Whether this email's people sign in with single sign-on (by its domain).
            sso.MapGet("", async (string email, SsoService service, CancellationToken ct) => new { available = await service.IsAvailableAsync(email, ct) });

            // A navigation, not a call: on to the provider, or back to the web app's sign-in with the reason.
            sso.MapGet("/start", async (string email, string returnTo, HttpContext http, SsoService service, SecurityOptions options, CloudOptions cloud, CancellationToken ct) =>
            {
                var web = WebAppOf(returnTo, http, options, cloud);
                if (web == null)
                {
                    return Results.Problem("returnTo must be the web app's address (Security:AllowedOrigins).", statusCode: StatusCodes.Status400BadRequest);
                }

                SsoStart start;
                try
                {
                    start = await service.StartAsync(email, AddressesOf(http, options), web, ct);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.IO.IOException || ex is System.Net.Http.HttpRequestException)
                {
                    return Results.Redirect(Back(web, error: "Your organization's identity provider could not be reached: try again, or ask an administrator."));
                }

                if (start == null)
                {
                    return Results.Redirect(Back(web, error: "Your email's organization does not sign in with single sign-on: use your password."));
                }

                // SAML answers with a cross-site POST, which only a SameSite=None (so Secure: HTTPS) cookie survives.
                http.Response.Cookies.Append(SsoService.BindingCookie, start.Binding, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = start.CrossSite || http.Request.IsHttps,
                    SameSite = start.CrossSite ? SameSiteMode.None : SameSiteMode.Lax,
                    Path = "/api/auth/sso",
                    MaxAge = SsoService.StateLifetime
                });
                return Results.Redirect(start.AuthorizationUrl);
            });

            sso.MapGet("/callback", async (HttpContext http, SsoService service, SecurityOptions options, AuditLog audit, CancellationToken ct) =>
            {
                var query = http.Request.Query;
                return Outcome(http, audit, await service.CompleteAsync(query["code"], query["state"], query["error"], http.Request.Cookies[SsoService.BindingCookie], AddressesOf(http, options), ct));
            });

            // SAML: this recorder as a service provider, for the identity provider's setup.
            sso.MapGet("/saml/metadata", (HttpContext http, SecurityOptions options) =>
            {
                var addresses = AddressesOf(http, options);
                var xml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><md:EntityDescriptor xmlns:md=\"urn:oasis:names:tc:SAML:2.0:metadata\" entityID=\"{System.Security.SecurityElement.Escape(addresses.SamlEntityId)}\">"
                    + "<md:SPSSODescriptor AuthnRequestsSigned=\"false\" WantAssertionsSigned=\"true\" protocolSupportEnumeration=\"urn:oasis:names:tc:SAML:2.0:protocol\">"
                    + "<md:NameIDFormat>urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress</md:NameIDFormat>"
                    + $"<md:AssertionConsumerService Binding=\"urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST\" Location=\"{System.Security.SecurityElement.Escape(addresses.SamlAcs)}\" index=\"0\" isDefault=\"true\"/>"
                    + "</md:SPSSODescriptor></md:EntityDescriptor>";
                return Results.Text(xml, "application/samlmetadata+xml");
            });

            // The provider's response, posted by the browser (a form from the provider's page: no antiforgery token).
            sso.MapPost("/saml/acs", async (HttpContext http, SsoService service, SecurityOptions options, AuditLog audit, CancellationToken ct) =>
            {
                var form = http.Request.HasFormContentType ? await http.Request.ReadFormAsync(ct) : null;
                return Outcome(http, audit, await service.CompleteSamlAsync(form?["SAMLResponse"], form?["RelayState"], http.Request.Cookies[SsoService.BindingCookie], AddressesOf(http, options), ct));
            }).DisableAntiforgery();

            // The web app's one-time code for its tokens.
            sso.MapPost("/exchange", async (ExchangeRequest request, SsoService service, CancellationToken ct) =>
                await service.ExchangeAsync(request?.Code, ct) is Member member
                    ? AuthEndpoints.SignIn(member)
                    : Results.Problem("This sign-in link was used already, or took too long: sign in again.", statusCode: StatusCodes.Status401Unauthorized));

            return app;
        }

        private static object ToDto(SsoSettings settings) => new
        {
            enabled = settings.Enabled,
            protocol = settings.Protocol,
            samlEntityId = settings.SamlEntityId,
            samlSsoUrl = settings.SamlSsoUrl,
            samlCertificate = settings.SamlCertificate,
            samlCertificateSubject = settings.SamlCertificateSubject,
            samlCertificateExpires = settings.SamlCertificateExpires,
            issuer = settings.Issuer,
            clientId = settings.ClientId,
            hasClientSecret = settings.HasClientSecret,
            domains = settings.Domains,
            autoCreate = settings.AutoCreate,
            defaultRole = settings.DefaultRole,
            required = settings.Required
        };

        /// <summary>This recorder's addresses for providers ("Security:PublicUrl", else this request's address).</summary>
        private static SsoAddresses AddressesOf(HttpContext http, SecurityOptions options)
        {
            var root = string.IsNullOrWhiteSpace(options.PublicUrl) ? $"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}" : options.PublicUrl.TrimEnd('/');
            return new SsoAddresses(root + CallbackPath, root + SamlAcsPath, root + SamlMetadataPath);
        }

        /// <summary>Back to the web app with the one-time code or the reason; audited either way.</summary>
        private static IResult Outcome(HttpContext http, AuditLog audit, (string ReturnTo, string Code, string Error, Member Member) result)
        {
            http.Response.Cookies.Delete(SsoService.BindingCookie, new CookieOptions { Path = "/api/auth/sso" });
            if (result.ReturnTo == null)
            {
                return Results.Problem("This sign-in did not start in this browser, or not here: start it again from the sign-in page.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (result.Member == null)
            {
                audit.Record(http, (Member)null, null, null, AuditActions.SignInFailed, new { singleSignOn = true, reason = result.Error });
                return Results.Redirect(Back(result.ReturnTo, error: result.Error));
            }

            audit.Record(http, result.Member, null, null, AuditActions.SignIn, new { singleSignOn = true });
            return Results.Redirect(Back(result.ReturnTo, code: result.Code));
        }

        /// <summary>The web app to go back to: one this recorder serves (its allowed origins, the cloud's address, or its own).</summary>
        private static string WebAppOf(string returnTo, HttpContext http, SecurityOptions options, CloudOptions cloud)
        {
            if (!Uri.TryCreate(returnTo, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return null;
            }

            var origin = uri.GetLeftPart(UriPartial.Authority);
            var allowed = options.AllowedOrigins.Select(o => o.TrimEnd('/'))
                .Append(cloud?.WebUrl)
                .Append($"{http.Request.Scheme}://{http.Request.Host}");
            return allowed.Any(a => string.Equals(a, origin, StringComparison.OrdinalIgnoreCase)) ? origin : null;
        }

        /// <summary>The web app's page for the outcome: <c>/sso#code=</c> (kept out of logs), or sign-in with the reason.</summary>
        private static string Back(string web, string code = null, string error = null) =>
            code != null ? $"{web}/sso#code={Uri.EscapeDataString(code)}" : $"{web}/login?sso_error={Uri.EscapeDataString(error ?? "Signing in did not work.")}";
    }
}
