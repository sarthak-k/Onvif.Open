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
using Onvif.Open.Recorder.Audit;
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

            /// <summary>The sites the user may see; empty: all sites. Null on a change: unchanged.</summary>
            public System.Collections.Generic.List<string> Sites { get; set; }
        }

        #endregion

        #region Public Methods

        public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder app)
        {
            var auth = app.MapGroup("/api/auth");

            // { "tokenType": "Bearer", "accessToken": "...", "expiresIn": 900, "refreshToken": "..." }
            auth.MapPost("/login", async (SignInRequest request, UserService users, AuditLog audit, HttpContext http, CancellationToken ct) =>
            {
                var member = await users.SignInAsync(request?.UserName, request?.Password, ct);

                // A failed sign-in of a known user goes to that user's organization; unknown names are kept without one.
                var known = member == null ? await users.FindByNameAsync(request?.UserName, ct) : null;
                audit.Record(http, member, known?.Membership.OrganizationId, request?.UserName, member == null ? AuditActions.SignInFailed : AuditActions.SignIn);

                return member == null
                    ? Results.Problem("Wrong user name or password, or the account is disabled or locked for a few minutes.", statusCode: StatusCodes.Status401Unauthorized)
                    : SignIn(member);
            }).AllowAnonymous().RequireRateLimiting(SecurityConfiguration.SignInLimit);

            auth.MapPost("/refresh", async (RefreshRequest request, IOptionsMonitor<BearerTokenOptions> bearer, UserService users, TimeProvider clock, CancellationToken ct) =>
            {
                var ticket = string.IsNullOrEmpty(request?.RefreshToken) ? null : bearer.Get(SecurityConfiguration.Scheme).RefreshTokenProtector.Unprotect(request.RefreshToken);

                if (ticket?.Properties?.ExpiresUtc is not DateTimeOffset expires || clock.GetUtcNow() >= expires)
                {
                    return Results.Problem("Sign in again.", statusCode: StatusCodes.Status401Unauthorized);
                }

                // The user may have been disabled, or changed role, sites or password, since the token was issued.
                var member = await users.FindMemberAsync(ticket.Principal.FindFirstValue(RecorderClaims.Organization), ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier), ct);
                if (member == null || !member.User.Enabled || !string.Equals(member.User.SecurityStamp, ticket.Principal.FindFirstValue(RecorderClaims.SecurityStamp), StringComparison.Ordinal))
                {
                    return Results.Problem("Sign in again.", statusCode: StatusCodes.Status401Unauthorized);
                }

                return SignIn(member);
            }).AllowAnonymous().RequireRateLimiting(SecurityConfiguration.SignInLimit);

            // The signed-in user, their role and sites, and their organization.
            auth.MapGet("/me", async (ClaimsPrincipal principal, UserService users, Tenancy.TenancyService tenancy, CancellationToken ct) =>
            {
                var member = await users.FindMemberAsync(principal.FindFirstValue(RecorderClaims.Organization), principal.FindFirstValue(ClaimTypes.NameIdentifier), ct);
                if (member == null)
                {
                    return Results.Unauthorized();
                }

                var organization = await tenancy.GetOrganizationAsync(member.Membership.OrganizationId, ct);
                var dto = ToDto(member);
                return Results.Ok(new
                {
                    dto.id,
                    dto.userName,
                    dto.role,
                    dto.sites,
                    dto.enabled,
                    dto.mustChangePassword,
                    dto.lockedUntil,
                    dto.lastLoginAt,
                    dto.createdAt,
                    organization = new { id = organization.Id, name = organization.Name, region = organization.Region }
                });
            }).RequireAuthorization(Policies.SignedIn);

            // Returns new tokens: the change ends every session, also this one.
            auth.MapPost("/password", async (PasswordRequest request, ClaimsPrincipal principal, UserService users, UserValidator validator, AuditLog audit, HttpContext http, CancellationToken ct) =>
            {
                var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var result = await users.ChangePasswordAsync(id, request?.CurrentPassword, request?.NewPassword, ct);
                validator.Invalidate(id);

                if (result.Status == UserStatus.Ok)
                {
                    audit.Record(http, AuditActions.PasswordChange, AuditTargets.User, id, result.Member.User.UserName);
                }

                return result.Status == UserStatus.Ok ? SignIn(result.Member) : ToResult(result);
            }).RequireAuthorization(Policies.SignedIn);

            // Members of the signed-in user's organization; managed by administrators who see all sites.
            var usersApi = app.MapGroup("/api/users").RequireAuthorization(Policies.OrganizationAdmin);

            usersApi.MapGet("", async (ClaimsPrincipal principal, UserService users, CancellationToken ct) =>
                (await users.GetMembersAsync(principal.FindFirstValue(RecorderClaims.Organization), ct)).Select(ToDto));

            usersApi.MapPost("", async (UserRequest request, ClaimsPrincipal principal, UserService users, CancellationToken ct) =>
            {
                // Created by an administrator: the user chooses an own password at the first sign-in.
                var result = await users.CreateMemberAsync(principal.FindFirstValue(RecorderClaims.Organization), principal.FindFirstValue(ClaimTypes.Role),
                    request?.UserName, request?.Password, request?.Role ?? Roles.Viewer, request?.Sites, mustChangePassword: true, ct);
                return result.Status == UserStatus.Ok ? Results.Created($"/api/users/{result.Member.User.Id}", ToDto(result.Member)) : ToResult(result);
            })
                .Audit(AuditActions.UserCreate, AuditTargets.User, details: c => c.Arguments[0] is UserRequest r ? new { role = r.Role, sites = r.Sites } : null);

            usersApi.MapPut("/{id}", async (string id, UserRequest request, ClaimsPrincipal principal, UserService users, UserValidator validator, CancellationToken ct) =>
            {
                var result = await users.UpdateMemberAsync(principal.FindFirstValue(RecorderClaims.Organization), principal.FindFirstValue(ClaimTypes.Role),
                    id, request?.Role, request?.Sites, request?.Enabled, request?.Password, ct);
                validator.Invalidate(id);
                return result.Status == UserStatus.Ok ? Results.Ok(ToDto(result.Member)) : ToResult(result);
            })
                .Audit(AuditActions.UserUpdate, AuditTargets.User, details: c => c.Arguments[1] is UserRequest r ? new { role = r.Role, sites = r.Sites, enabled = r.Enabled, passwordReset = r.Password != null } : null);

            usersApi.MapDelete("/{id}", async (string id, ClaimsPrincipal principal, UserService users, UserValidator validator, CancellationToken ct) =>
            {
                var result = await users.DeleteMemberAsync(principal.FindFirstValue(RecorderClaims.Organization), principal.FindFirstValue(ClaimTypes.Role),
                    id, principal.FindFirstValue(ClaimTypes.NameIdentifier), ct);
                validator.Invalidate(id);
                return result.Status == UserStatus.Ok ? Results.NoContent() : ToResult(result);
            })
                .Audit(AuditActions.UserDelete, AuditTargets.User);

            return app;
        }

        /// <summary>Issues tokens for the member (access + refresh).</summary>
        public static IResult SignIn(Member member)
        {
            return Results.SignIn(UserService.CreatePrincipal(member, SecurityConfiguration.Scheme), authenticationScheme: SecurityConfiguration.Scheme);
        }

        #endregion

        #region Private Methods

        private static UserDto ToDto(Member member)
        {
            var user = member.User;
            return new UserDto(
                user.Id,
                user.UserName,
                member.Membership.Role,
                member.Sites,
                user.Enabled,
                user.MustChangePassword,
                user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow ? new DateTimeOffset(DateTime.SpecifyKind(user.LockedUntil.Value, DateTimeKind.Utc)) : null,
                user.LastLoginAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(user.LastLoginAt.Value, DateTimeKind.Utc)) : null,
                new DateTimeOffset(DateTime.SpecifyKind(user.CreatedAt, DateTimeKind.Utc)));
        }

        private sealed record UserDto(string id, string userName, string role, System.Collections.Generic.IReadOnlyList<string> sites, bool enabled, bool mustChangePassword, DateTimeOffset? lockedUntil, DateTimeOffset? lastLoginAt, DateTimeOffset createdAt);

        private static IResult ToResult(UserResult result)
        {
            switch (result.Status)
            {
                case UserStatus.NotFound:
                    return Results.NotFound();
                case UserStatus.Conflict:
                    return Results.Conflict(new { errors = result.Errors });
                case UserStatus.Forbidden:
                    return Results.Json(new { errors = result.Errors }, statusCode: StatusCodes.Status403Forbidden);
                default:
                    return Results.BadRequest(new { errors = result.Errors });
            }
        }

        #endregion
    }
}
