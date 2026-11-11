// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Security
{
    /// <summary>What an organization's single sign-on is set to (the client secret is never returned).</summary>
    public sealed class SsoSettings
    {
        public bool Enabled { get; set; }
        public string Issuer { get; set; }
        public string ClientId { get; set; }

        /// <summary>On a change: null keeps the stored secret.</summary>
        public string ClientSecret { get; set; }

        public bool HasClientSecret { get; set; }
        public List<string> Domains { get; set; } = new List<string>();
        public bool AutoCreate { get; set; }
        public string DefaultRole { get; set; } = Roles.Viewer;
        public bool Required { get; set; }
    }

    /// <summary>How the recorder reaches identity providers (tests use their own).</summary>
    public sealed class SsoHttp
    {
        public SsoHttp(HttpMessageHandler handler, bool requireHttps = true)
        {
            Client = new HttpClient(handler ?? throw new ArgumentNullException(nameof(handler))) { Timeout = TimeSpan.FromSeconds(15) };
            RequireHttps = requireHttps;
        }

        public HttpClient Client { get; }

        /// <summary>Providers must use HTTPS (off only for tests).</summary>
        public bool RequireHttps { get; }
    }

    /// <summary>Where a sign-in started: the browser goes there, then comes back to the web app.</summary>
    public sealed record SsoStart(string AuthorizationUrl, string Binding);

    /// <summary>
    /// Single sign-on with OpenID Connect, one identity provider per organization, found by the email's domain. The
    /// browser goes to the provider (authorization code with PKCE, a nonce, and state bound to the browser by a
    /// cookie); the recorder exchanges the code itself, checks the ID token (signature from the provider's keys,
    /// issuer, audience, lifetime, nonce) and hands the web app a one-time code for its tokens. Accounts are linked by
    /// the provider's subject; at the first sign-in, to the member with that (verified) email, or created where the
    /// organization lets it. A provider only ever signs in members of its own organization.
    /// </summary>
    public sealed class SsoService
    {
        #region Constructor

        public SsoService(IDbContextFactory<RecorderDbContext> contexts, UserService users, IDataProtectionProvider dataProtection, IMemoryCache cache, SsoHttp http, ILogger<SsoService> logger, TimeProvider timeProvider = null)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _users = users ?? throw new ArgumentNullException(nameof(users));
            var protection = dataProtection ?? throw new ArgumentNullException(nameof(dataProtection));
            _secrets = protection.CreateProtector("Onvif.Open.Recorder.SsoClientSecret.v1");
            _states = protection.CreateProtector("Onvif.Open.Recorder.SsoState.v1");
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Public Variables

        public const int MaxDomains = 20;

        /// <summary>How long the trip to the provider may take.</summary>
        public static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

        /// <summary>How long the web app has to collect its tokens.</summary>
        public static readonly TimeSpan HandoffLifetime = TimeSpan.FromMinutes(1);

        /// <summary>The cookie that ties the provider's answer to the browser that asked (against login CSRF).</summary>
        public const string BindingCookie = "oo_sso";

        #endregion

        #region Private Variables

        private sealed record State(string OrganizationId, string Nonce, string Verifier, string ReturnTo, string BindingHash, long Expires);

        private sealed record Handoff(string OrganizationId, string UserId);

        private readonly IDbContextFactory<RecorderDbContext> _contexts;
        private readonly UserService _users;
        private readonly IDataProtector _secrets;
        private readonly IDataProtector _states;
        private readonly IMemoryCache _cache;
        private readonly SsoHttp _http;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _providers = new ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);

        #endregion

        #region Public Methods - settings

        public async Task<SsoSettings> GetAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = await db.SingleSignOn.AsNoTracking().FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken).ConfigureAwait(false);
                var domains = await db.SsoDomains.AsNoTracking().Where(d => d.OrganizationId == organizationId).Select(d => d.Domain).OrderBy(d => d).ToListAsync(cancellationToken).ConfigureAwait(false);
                return record == null
                    ? new SsoSettings { Domains = domains }
                    : new SsoSettings
                    {
                        Enabled = record.Enabled,
                        Issuer = record.Issuer,
                        ClientId = record.ClientId,
                        HasClientSecret = !string.IsNullOrEmpty(record.ClientSecret),
                        Domains = domains,
                        AutoCreate = record.AutoCreate,
                        DefaultRole = record.DefaultRole ?? Roles.Viewer,
                        Required = record.Required
                    };
            }
        }

        public async Task<ServiceResult<SsoSettings>> SaveAsync(string organizationId, SsoSettings request, CancellationToken cancellationToken = default)
        {
            if (request == null)
            {
                return ServiceResult<SsoSettings>.Invalid(new[] { "Send the single sign-on settings." });
            }

            var errors = new List<string>();
            var issuer = request.Issuer?.Trim().TrimEnd('/');
            var domains = (request.Domains ?? new List<string>()).Select(d => d?.Trim().TrimStart('@').ToLowerInvariant()).Where(d => !string.IsNullOrEmpty(d)).Distinct().ToList();
            var role = Roles.All.FirstOrDefault(r => string.Equals(r, request.DefaultRole, StringComparison.OrdinalIgnoreCase)) ?? (request.DefaultRole == null ? Roles.Viewer : null);

            if (request.Enabled || !string.IsNullOrEmpty(issuer))
            {
                if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && (_http.RequireHttps || uri.Scheme != Uri.UriSchemeHttp)) || !string.IsNullOrEmpty(uri.Query))
                {
                    errors.Add("Issuer must be the provider's https address (its discovery document is at /.well-known/openid-configuration below it).");
                }
            }

            if (request.Enabled && string.IsNullOrWhiteSpace(request.ClientId))
            {
                errors.Add("Client ID is required.");
            }

            if (domains.Count == 0 && request.Enabled)
            {
                errors.Add("Add the email domains whose people sign in this way, e.g. riverside-shops.com.");
            }

            if (domains.Count > MaxDomains || domains.Any(d => d.Length > 253 || Uri.CheckHostName(d) != UriHostNameType.Dns || !d.Contains('.')))
            {
                errors.Add($"Domains are names like riverside-shops.com, at most {MaxDomains}.");
            }

            if (role == null || role == Roles.Owner)
            {
                errors.Add("New accounts are Viewers, Operators or Admins.");
            }

            if (request.Required && !request.Enabled)
            {
                errors.Add("Turn single sign-on on to require it.");
            }

            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    var taken = await db.SsoDomains.AsNoTracking().Where(d => domains.Contains(d.Domain) && d.OrganizationId != organizationId).Select(d => d.Domain).ToListAsync(cancellationToken).ConfigureAwait(false);
                    if (taken.Count > 0)
                    {
                        errors.Add($"Another organization signs in {string.Join(", ", taken)} already.");
                    }

                    var record = await db.SingleSignOn.FirstOrDefaultAsync(s => s.OrganizationId == organizationId, cancellationToken).ConfigureAwait(false);
                    var secret = request.ClientSecret == null ? record?.ClientSecret : request.ClientSecret.Length == 0 ? null : _secrets.Protect(request.ClientSecret);
                    if (request.Enabled && secret == null)
                    {
                        errors.Add("Client secret is required.");
                    }

                    if (errors.Count > 0)
                    {
                        return ServiceResult<SsoSettings>.Invalid(errors);
                    }

                    if (record == null)
                    {
                        record = new SsoRecord { OrganizationId = organizationId };
                        db.SingleSignOn.Add(record);
                    }

                    record.Enabled = request.Enabled;
                    record.Issuer = string.IsNullOrEmpty(issuer) ? null : issuer;
                    record.ClientId = request.ClientId?.Trim();
                    record.ClientSecret = secret;
                    record.AutoCreate = request.AutoCreate;
                    record.DefaultRole = role;
                    record.Required = request.Required;
                    record.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

                    db.SsoDomains.RemoveRange(db.SsoDomains.Where(d => d.OrganizationId == organizationId));
                    db.SsoDomains.AddRange(domains.Select(d => new SsoDomainRecord { Domain = d, OrganizationId = organizationId }));
                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _changes.Release();
            }

            if (issuer != null)
            {
                _providers.TryRemove(issuer, out _);
            }

            return ServiceResult<SsoSettings>.Ok(await GetAsync(organizationId, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Whether the organization's members sign in only with single sign-on (owners keep their password).</summary>
        public async Task<bool> IsRequiredAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.SingleSignOn.AnyAsync(s => s.OrganizationId == organizationId && s.Enabled && s.Required, cancellationToken).ConfigureAwait(false);
            }
        }

        #endregion

        #region Public Methods - signing in

        /// <summary>Whether people with this email sign in with single sign-on (their domain's organization has it on).</summary>
        public async Task<bool> IsAvailableAsync(string email, CancellationToken cancellationToken = default) =>
            await FindByEmailAsync(email, cancellationToken).ConfigureAwait(false) != null;

        /// <summary>The provider's sign-in page for this email; null when its domain has no single sign-on.</summary>
        public async Task<SsoStart> StartAsync(string email, string redirectUri, string returnTo, CancellationToken cancellationToken = default)
        {
            var sso = await FindByEmailAsync(email, cancellationToken).ConfigureAwait(false);
            if (sso == null)
            {
                return null;
            }

            var provider = await ConfigurationOf(sso.Issuer).GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            var binding = Random(32);
            var verifier = Random(32);
            var nonce = Random(16);
            var state = _states.Protect(JsonSerializer.Serialize(new State(sso.OrganizationId, nonce, verifier, returnTo, Hash(binding), (_timeProvider.GetUtcNow() + StateLifetime).ToUnixTimeSeconds())));

            var query = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = sso.ClientId,
                ["redirect_uri"] = redirectUri,
                ["scope"] = "openid email profile",
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
                ["code_challenge_method"] = "S256",
                ["login_hint"] = email.Trim()
            };
            var separator = provider.AuthorizationEndpoint.Contains('?') ? "&" : "?";
            return new SsoStart(provider.AuthorizationEndpoint + separator + string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")), binding);
        }

        /// <summary>
        /// The provider's answer: on success the web app address with a one-time code for the tokens, else with the
        /// reason. Null when the state is not one this recorder made for this browser (nowhere safe to send it back).
        /// </summary>
        public async Task<(string ReturnTo, string Code, string Error, Member Member)> CompleteAsync(string code, string stateText, string error, string binding, string redirectUri, CancellationToken cancellationToken = default)
        {
            State state;
            try
            {
                state = string.IsNullOrEmpty(stateText) ? null : JsonSerializer.Deserialize<State>(_states.Unprotect(stateText));
            }
            catch (Exception ex) when (ex is CryptographicException || ex is JsonException)
            {
                state = null;
            }

            if (state == null || string.IsNullOrEmpty(binding) || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(state.BindingHash), Encoding.ASCII.GetBytes(Hash(binding))))
            {
                return (null, null, null, null);
            }

            if (_timeProvider.GetUtcNow().ToUnixTimeSeconds() >= state.Expires)
            {
                return (state.ReturnTo, null, "Signing in took too long: try again.", null);
            }

            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            {
                return (state.ReturnTo, null, "The identity provider did not sign you in" + (string.IsNullOrEmpty(error) ? "." : $" ({Truncate(error, 100)})."), null);
            }

            SsoRecord sso;
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                sso = await db.SingleSignOn.AsNoTracking().FirstOrDefaultAsync(s => s.OrganizationId == state.OrganizationId && s.Enabled, cancellationToken).ConfigureAwait(false);
            }

            if (sso == null)
            {
                return (state.ReturnTo, null, "Single sign-on is off for your organization.", null);
            }

            JsonWebToken identity;
            try
            {
                identity = await RedeemAsync(sso, code, state, redirectUri, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is SecurityTokenException || ex is InvalidOperationException || ex is JsonException || ex is TaskCanceledException || ex is ArgumentException)
            {
                _logger?.LogWarning(ex, "Single sign-on with {Issuer} failed", sso.Issuer);
                return (state.ReturnTo, null, "The identity provider's answer could not be used: try again, or ask an administrator.", null);
            }

            var member = await ResolveAsync(sso, identity, cancellationToken).ConfigureAwait(false);
            if (member.Error != null)
            {
                return (state.ReturnTo, null, member.Error, null);
            }

            var handoff = Random(32);
            _cache.Set("sso-handoff:" + Hash(handoff), new Handoff(member.Member.Membership.OrganizationId, member.Member.User.Id), HandoffLifetime);
            return (state.ReturnTo, handoff, null, member.Member);
        }

        /// <summary>The member a one-time code from <see cref="CompleteAsync"/> is for (once), or null.</summary>
        public async Task<Member> ExchangeAsync(string handoff, CancellationToken cancellationToken = default)
        {
            var key = "sso-handoff:" + Hash(handoff ?? string.Empty);
            if (string.IsNullOrEmpty(handoff) || !_cache.TryGetValue(key, out Handoff found))
            {
                return null;
            }

            _cache.Remove(key);
            var member = await _users.FindMemberAsync(found.OrganizationId, found.UserId, cancellationToken).ConfigureAwait(false);
            return member == null || !_users.CanSignIn(member.User) ? null : await _users.WithPolicyAsync(member with { ViaSingleSignOn = true }, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Private Methods

        private async Task<SsoRecord> FindByEmailAsync(string email, CancellationToken cancellationToken)
        {
            var at = email?.Trim().LastIndexOf('@') ?? -1;
            if (at <= 0)
            {
                return null;
            }

            var domain = email.Trim().Substring(at + 1).ToLowerInvariant();
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var organizationId = await db.SsoDomains.AsNoTracking().Where(d => d.Domain == domain).Select(d => d.OrganizationId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                return organizationId == null ? null : await db.SingleSignOn.AsNoTracking().FirstOrDefaultAsync(s => s.OrganizationId == organizationId && s.Enabled, cancellationToken).ConfigureAwait(false);
            }
        }

        private ConfigurationManager<OpenIdConnectConfiguration> ConfigurationOf(string issuer) =>
            _providers.GetOrAdd(issuer, i => new ConfigurationManager<OpenIdConnectConfiguration>(
                i + "/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(_http.Client) { RequireHttps = _http.RequireHttps }));

        /// <summary>The code for tokens at the provider, and its ID token checked.</summary>
        private async Task<JsonWebToken> RedeemAsync(SsoRecord sso, string code, State state, string redirectUri, CancellationToken cancellationToken)
        {
            var configuration = ConfigurationOf(sso.Issuer);
            var provider = await configuration.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

            using var response = await _http.Client.PostAsync(provider.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = sso.ClientId,
                ["client_secret"] = _secrets.Unprotect(sso.ClientSecret),
                ["code_verifier"] = state.Verifier
            }), cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"The token endpoint answered {(int)response.StatusCode}: {Truncate(body, 200)}");
            }

            var idToken = JsonDocument.Parse(body).RootElement.TryGetProperty("id_token", out var token) ? token.GetString() : null;
            var handler = new JsonWebTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidIssuer = provider.Issuer,
                ValidAudience = sso.ClientId,
                IssuerSigningKeys = provider.SigningKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2),
                LifetimeValidator = (notBefore, expires, _, _) =>
                {
                    var now = _timeProvider.GetUtcNow().UtcDateTime;
                    return expires != null && now < expires.Value.AddMinutes(2) && (notBefore == null || now >= notBefore.Value.AddMinutes(-2));
                }
            };

            var result = await handler.ValidateTokenAsync(idToken, parameters).ConfigureAwait(false);
            if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                // The provider may have rolled its keys.
                configuration.RequestRefresh();
                parameters.IssuerSigningKeys = (await configuration.GetConfigurationAsync(cancellationToken).ConfigureAwait(false)).SigningKeys;
                result = await handler.ValidateTokenAsync(idToken, parameters).ConfigureAwait(false);
            }

            if (!result.IsValid)
            {
                throw result.Exception as SecurityTokenException ?? new SecurityTokenException("The ID token is not valid.");
            }

            var jwt = (JsonWebToken)result.SecurityToken;
            if (!jwt.TryGetPayloadValue<string>("nonce", out var nonce) || !string.Equals(nonce, state.Nonce, StringComparison.Ordinal))
            {
                throw new SecurityTokenException("The ID token is not for this sign-in.");
            }

            return jwt;
        }

        /// <summary>The member the provider vouched for: linked before, the one with that email, or a new one.</summary>
        private async Task<(Member Member, string Error)> ResolveAsync(SsoRecord sso, JsonWebToken identity, CancellationToken cancellationToken)
        {
            var issuer = identity.Issuer;
            var subject = identity.Subject;
            identity.TryGetPayloadValue<string>("email", out var email);
            var verified = identity.TryGetPayloadValue<bool>("email_verified", out var flag) ? flag : false;
            email = email?.Trim();
            if (string.IsNullOrEmpty(subject))
            {
                return (null, "The identity provider did not say who you are.");
            }

            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    UserRecord user = null;
                    var link = await db.ExternalLogins.FirstOrDefaultAsync(l => l.Issuer == issuer && l.Subject == subject, cancellationToken).ConfigureAwait(false);
                    if (link != null)
                    {
                        user = await _users.FindAsync(link.UserId, cancellationToken).ConfigureAwait(false);
                        if (user == null)
                        {
                            db.ExternalLogins.Remove(link);
                            link = null;
                        }
                    }

                    if (user == null)
                    {
                        if (string.IsNullOrEmpty(email) || !verified)
                        {
                            return (null, "The identity provider did not confirm your email address.");
                        }

                        user = await _users.FindAsync((await _users.FindByNameAsync(email, cancellationToken).ConfigureAwait(false))?.User.Id, cancellationToken).ConfigureAwait(false);
                        if (user == null)
                        {
                            var domain = email.Substring(email.LastIndexOf('@') + 1).ToLowerInvariant();
                            var ours = await db.SsoDomains.AnyAsync(d => d.Domain == domain && d.OrganizationId == sso.OrganizationId, cancellationToken).ConfigureAwait(false);
                            if (!sso.AutoCreate || !ours)
                            {
                                return (null, $"There is no account for {email}: ask an administrator to add you.");
                            }

                            var created = await _users.CreateMemberAsync(sso.OrganizationId, Roles.Owner, email, NewUnusablePassword(), sso.DefaultRole ?? Roles.Viewer, null, mustChangePassword: false, cancellationToken).ConfigureAwait(false);
                            if (created.Status != UserStatus.Ok)
                            {
                                return (null, $"An account for {email} could not be made: {string.Join(" ", created.Errors ?? Array.Empty<string>())}");
                            }

                            user = created.Member.User;
                            _logger?.LogInformation("Account {UserName} made at its first single sign-on", email);
                        }
                    }

                    // A provider signs in only members of its own organization.
                    var membership = await _users.FindHomeMembershipAsync(user.Id, cancellationToken).ConfigureAwait(false);
                    if (membership?.OrganizationId != sso.OrganizationId)
                    {
                        return (null, "This account belongs to another organization: sign in there.");
                    }

                    if (!_users.CanSignIn(user))
                    {
                        return (null, "This account is disabled or locked for a few minutes.");
                    }

                    if (link == null)
                    {
                        db.ExternalLogins.Add(new ExternalLoginRecord { Id = Guid.NewGuid().ToString("D"), UserId = user.Id, Issuer = issuer, Subject = subject, CreatedAt = _timeProvider.GetUtcNow().UtcDateTime });
                    }

                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await _users.RecordSignInAsync(user, cancellationToken).ConfigureAwait(false);
                    return (new Member(user, membership), null);
                }
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>A password nobody knows: accounts made by single sign-on sign in with it alone (an administrator can set one).</summary>
        private static string NewUnusablePassword() => "Sso-" + Random(32) + "-9a";

        private static string Random(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        private static string Truncate(string text, int length) => text == null || text.Length <= length ? text : text.Substring(0, length) + "…";

        #endregion
    }
}
