// Sarthak Killedar
// 2026-11-08

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Security
{
    /// <summary>
    /// Authentication (bearer access + refresh tokens, protected with the recorder's Data Protection keys),
    /// role policies, CORS for the web application and the sign-in rate limit.
    /// </summary>
    public static class SecurityConfiguration
    {
        #region Public Variables

        public const string Scheme = BearerTokenDefaults.AuthenticationScheme;
        public const string CorsPolicy = "WebApplication";
        public const string SignInLimit = "SignIn";

        /// <summary>Query parameter that carries the access token where a header cannot (video players, downloads).</summary>
        public const string AccessTokenParameter = "access_token";

        #endregion

        #region Public Methods

        public static IServiceCollection AddRecorderSecurity(this IServiceCollection services, IConfiguration configuration)
        {
            var options = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();

            services.AddSingleton(options);
            services.AddSingleton<IUserStore, EfUserStore>();
            services.AddSingleton<UserService>();
            services.AddSingleton<TwoFactorService>();
            services.AddSingleton<UserValidator>();
            services.AddMemoryCache();
            services.AddHostedService<SecurityStartup>();

            services.AddAuthentication(Scheme).AddBearerToken(Scheme, bearer =>
            {
                bearer.BearerTokenExpiration = options.AccessTokenLifetime;
                bearer.RefreshTokenExpiration = options.RefreshTokenLifetime;
                bearer.Events = new BearerTokenEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Only for reading video: a token in the URL ends up in logs and browser history.
                        if (IsMediaRequest(context.Request) && context.Request.Query.TryGetValue(AccessTokenParameter, out var token))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    }
                };
            });

            services.AddSingleton<IAuthorizationHandler, PasswordChangedHandler>();
            services.AddAuthorization(authorization =>
            {
                authorization.AddPolicy(Policies.SignedIn, policy => policy.RequireAuthenticatedUser());

                foreach (var role in Roles.All)
                {
                    authorization.AddPolicy(role, policy => policy
                        .RequireAuthenticatedUser()
                        .RequireRole(Roles.AtLeast(role))
                        .AddRequirements(new PasswordChangedRequirement()));
                }

                authorization.AddPolicy(Policies.ControlPlane, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireClaim(RecorderClaims.Tunnel));

                authorization.AddPolicy(Policies.OrganizationAdmin, policy => policy
                    .RequireAuthenticatedUser()
                    .RequireRole(Roles.AtLeast(Roles.Admin))
                    .RequireAssertion(context => !context.User.HasClaim(c => c.Type == RecorderClaims.Sites))
                    .AddRequirements(new PasswordChangedRequirement()));

                // Secure by default: an endpoint without its own policy needs a signed-in user.
                authorization.FallbackPolicy = authorization.GetPolicy(Policies.Viewer);
            });

            services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy =>
            {
                if (options.AllowedOrigins.Count > 0)
                {
                    policy.WithOrigins(options.AllowedOrigins.Select(o => o.TrimEnd('/')).ToArray())
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .WithExposedHeaders("Content-Disposition", "Location", Api.PlaybackEndpoints.CodecHeader, Api.PlaybackEndpoints.StreamHeader);
                }
            }));

            services.AddRateLimiter(limiter =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                limiter.AddPolicy(SignInLimit, http => RateLimitPartition.GetFixedWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(1, options.SignInsPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            });

            return services;
        }

        /// <summary>CORS, authentication, token revocation checks, authorization and rate limiting, in that order.</summary>
        public static WebApplication UseRecorderSecurity(this WebApplication app)
        {
            app.UseCors(CorsPolicy);
            app.UseAuthentication();
            app.Use(async (context, next) =>
            {
                // Requests through the tunnel act as the control plane, which checked its own user already.
                if (context.User.Identity?.IsAuthenticated == true && !Tunnel.TunnelExecutor.IsControlPlane(context.User) && !await context.RequestServices.GetRequiredService<UserValidator>().IsValidAsync(context.User, context.RequestAborted))
                {
                    // Signed out everywhere, password or role changed, or disabled: the client must refresh or sign in.
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
                    return;
                }

                await next();
            });
            app.UseAuthorization();
            app.UseRateLimiter();
            return app;
        }

        /// <summary>GET requests that video players and downloads make (playlists, segments, clips, snapshots, exports).</summary>
        public static bool IsMediaRequest(HttpRequest request)
        {
            if (!HttpMethods.IsGet(request.Method))
            {
                return false;
            }

            var path = request.Path.Value ?? string.Empty;
            return path.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("/clip", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("/snapshot", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith("/export", StringComparison.OrdinalIgnoreCase)
                   || path.Contains("/segments/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A URI inside a playlist: when the playlist was requested with a token in the URL (a native player),
        /// the segments need it too.
        /// </summary>
        public static string WithMediaToken(HttpContext context, string uri)
        {
            if (!context.Request.Query.TryGetValue(AccessTokenParameter, out var token) || string.IsNullOrEmpty(token))
            {
                return uri;
            }

            return uri + (uri.Contains('?') ? "&" : "?") + AccessTokenParameter + "=" + Uri.EscapeDataString(token.ToString());
        }

        #endregion
    }

    /// <summary>
    /// Checks that a token's user still exists, is enabled and has the same security stamp (cached for 30 s,
    /// so revocation takes effect within that time without a database query per video segment).
    /// </summary>
    public sealed class UserValidator
    {
        public UserValidator(IUserStore store, IMemoryCache cache)
        {
            _store = store;
            _cache = cache;
        }

        public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        private readonly IUserStore _store;
        private readonly IMemoryCache _cache;

        public async Task<bool> IsValidAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
        {
            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var stamp = principal.FindFirstValue(RecorderClaims.SecurityStamp);

            if (id == null || stamp == null)
            {
                return false;
            }

            var current = await _cache.GetOrCreateAsync("user-stamp:" + id, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                var user = await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
                return user == null || !user.Enabled ? null : user.SecurityStamp;
            }).ConfigureAwait(false);

            return string.Equals(current, stamp, StringComparison.Ordinal);
        }

        /// <summary>Forgets the cached state of a user right away (after changes made through this recorder).</summary>
        public void Invalidate(string id)
        {
            _cache.Remove("user-stamp:" + id);
        }
    }

    /// <summary>
    /// A user whose password was generated or reset must change it before using the API, and one whose organization
    /// requires a second factor must set it up (only <see cref="Policies.SignedIn"/> endpoints answer until then).
    /// </summary>
    public sealed class PasswordChangedRequirement : IAuthorizationRequirement
    {
    }

    public sealed class PasswordChangedHandler : AuthorizationHandler<PasswordChangedRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PasswordChangedRequirement requirement)
        {
            if (!context.User.HasClaim(c => c.Type == RecorderClaims.MustChangePassword || c.Type == RecorderClaims.MustSetUpTwoFactor))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Automated installs: creates the configured owner at startup when there are no users (else the web application's setup does).</summary>
    internal sealed class SecurityStartup : IHostedService
    {
        public SecurityStartup(StartupSettings settings, Tenancy.TenancyService tenancy)
        {
            _tenancy = tenancy;
        }

        private readonly Tenancy.TenancyService _tenancy;

        public Task StartAsync(CancellationToken cancellationToken) => _tenancy.EnsureConfiguredOwnerAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
