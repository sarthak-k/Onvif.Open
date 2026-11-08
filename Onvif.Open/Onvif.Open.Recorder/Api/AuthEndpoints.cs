// Sarthak Killedar
// 2026-11-08

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: sign-in (access + refresh token), the own account, and user management (admins).
    /// </summary>
    internal static class AuthEndpoints
    {
        #region Public Variables

        public sealed class SignInRequest
        {
            public string UserName { get; set; }
            public string Password { get; set; }
        }

        public sealed class RefreshRequest
        {
            public string RefreshToken { get; set; }
        }

        public sealed class PasswordRequest
        {
            public string CurrentPassword { get; set; }
            public string NewPassword { get; set; }
        }

        public sealed class UserRequest
        {
            public string UserName { get; set; }
            public string Password { get; set; }
            public string Role { get; set; }
            public bool? Enabled { get; set; }
        }

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
        {
            var auth = app.MapGroup("/api/auth");

            // { "tokenType": "Bearer", "accessToken": "...", "expiresIn": 900, "refreshToken": "..." }
            auth.MapPost("/login", async (SignInRequest request, UserService users, CancellationToken ct) =>
            {
                var user = await users.SignInAsync(request?.UserName, request?.Password, ct);
                return user == null
                    ? Results.Problem("Wrong user name or password, or the account is disabled or locked for a few minutes.", statusCode: StatusCodes.Status401Unauthorized)
                    : Results.SignIn(UserService.CreatePrincipal(user, SecurityConfiguration.Scheme), authenticationScheme: SecurityConfiguration.Scheme);
            }).AllowAnonymous().RequireRateLimiting(SecurityConfiguration.SignInLimit);

            auth.MapPost("/refresh", async (RefreshRequest request, IOptionsMonitor<BearerTokenOptions> bearer, UserService users, TimeProvider clock, CancellationToken ct) =>
            {
                var ticket = string.IsNullOrEmpty(request?.RefreshToken) ? null : bearer.Get(SecurityConfiguration.Scheme).RefreshTokenProtector.Unprotect(request.RefreshToken);

                if (ticket?.Properties?.ExpiresUtc is not DateTimeOffset expires || clock.GetUtcNow() >= expires)
                {
                    return Results.Problem("Sign in again.", statusCode: StatusCodes.Status401Unauthorized);
                }

                // The user may have been disabled, or changed role or password, since the token was issued.
                var user = await users.FindAsync(ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier), ct);
                if (user == null || !user.Enabled || !string.Equals(user.SecurityStamp, ticket.Principal.FindFirstValue(RecorderClaims.SecurityStamp), StringComparison.Ordinal))
                {
                    return Results.Problem("Sign in again.", statusCode: StatusCodes.Status401Unauthorized);
                }

                return Results.SignIn(UserService.CreatePrincipal(user, SecurityConfiguration.Scheme), authenticationScheme: SecurityConfiguration.Scheme);
            }).AllowAnonymous().RequireRateLimiting(SecurityConfiguration.SignInLimit);

            auth.MapGet("/me", async (ClaimsPrincipal principal, UserService users, CancellationToken ct) =>
            {
                var user = await users.FindAsync(principal.FindFirstValue(ClaimTypes.NameIdentifier), ct);
                return user == null ? Results.Unauthorized() : Results.Ok(ToDto(user));
            }).RequireAuthorization(Policies.SignedIn);

            // Returns new tokens: the change ends every session, also this one.
            auth.MapPost("/password", async (PasswordRequest request, ClaimsPrincipal principal, UserService users, UserValidator validator, CancellationToken ct) =>
            {
                var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var result = await users.ChangePasswordAsync(id, request?.CurrentPassword, request?.NewPassword, ct);
                validator.Invalidate(id);

                return result.Status == UserStatus.Ok
                    ? Results.SignIn(UserService.CreatePrincipal(result.User, SecurityConfiguration.Scheme), authenticationScheme: SecurityConfiguration.Scheme)
                    : ToResult(result);
            }).RequireAuthorization(Policies.SignedIn);

            var usersApi = app.MapGroup("/api/users").RequireAuthorization(Policies.Admin);

            usersApi.MapGet("", async (UserService users, CancellationToken ct) => (await users.GetUsersAsync(ct)).Select(ToDto));

            usersApi.MapPost("", async (UserRequest request, UserService users, CancellationToken ct) =>
            {
                // Created by an admin: the user chooses an own password at the first sign-in.
                var result = await users.CreateAsync(request?.UserName, request?.Password, request?.Role ?? Roles.Viewer, mustChangePassword: true, ct);
                return result.Status == UserStatus.Ok ? Results.Created($"/api/users/{result.User.Id}", ToDto(result.User)) : ToResult(result);
            });

            usersApi.MapPut("/{id}", async (string id, UserRequest request, UserService users, UserValidator validator, CancellationToken ct) =>
            {
                var result = await users.UpdateAsync(id, request?.Role, request?.Enabled, request?.Password, ct);
                validator.Invalidate(id);
                return result.Status == UserStatus.Ok ? Results.Ok(ToDto(result.User)) : ToResult(result);
            });

            usersApi.MapDelete("/{id}", async (string id, ClaimsPrincipal principal, UserService users, UserValidator validator, CancellationToken ct) =>
            {
                var result = await users.DeleteAsync(id, principal.FindFirstValue(ClaimTypes.NameIdentifier), ct);
                validator.Invalidate(id);
                return result.Status == UserStatus.Ok ? Results.NoContent() : ToResult(result);
            });

            return app;
        }

        #endregion

        #region Private Methods

        private static object ToDto(UserRecord user)
        {
            return new
            {
                id = user.Id,
                userName = user.UserName,
                role = user.Role,
                enabled = user.Enabled,
                mustChangePassword = user.MustChangePassword,
                lockedUntil = user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow ? new DateTimeOffset(DateTime.SpecifyKind(user.LockedUntil.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null,
                lastLoginAt = user.LastLoginAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(user.LastLoginAt.Value, DateTimeKind.Utc)) : (DateTimeOffset?)null,
                createdAt = new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc))
            };
        }

        private static IResult ToResult(UserResult result)
        {
            switch (result.Status)
            {
                case UserStatus.NotFound:
                    return Results.NotFound();
                case UserStatus.Conflict:
                    return Results.Conflict(new { errors = result.Errors });
                default:
                    return Results.BadRequest(new { errors = result.Errors });
            }
        }

        #endregion
    }
}
