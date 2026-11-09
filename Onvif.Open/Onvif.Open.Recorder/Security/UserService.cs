// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Security
{
    /// <summary>A user with their role and sites in one organization.</summary>
    public sealed record Member(UserRecord User, MembershipRecord Membership)
    {
        /// <summary>The sites the member is limited to; empty: all sites.</summary>
        public IReadOnlyList<string> Sites => UserService.ReadSites(Membership.Sites);
    }

    /// <summary>
    /// Users and their memberships: sign-in with lockout, password rules, roles and site access per organization, and
    /// the guarantee that an enabled owner always remains. Every change to a user's access rotates its security
    /// stamp, which ends its sessions.
    /// </summary>
    public sealed partial class UserService
    {
        #region Constructor

        public UserService(IUserStore store, ITenancyStore tenancy, SecurityOptions options, ILogger<UserService> logger, TimeProvider timeProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _tenancy = tenancy ?? throw new ArgumentNullException(nameof(tenancy));
            _options = options ?? new SecurityOptions();
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Private Variables

        public const int MinPasswordLength = 10;
        private const int MaxPasswordLength = 256;

        /// <summary>Source generated: matched without compiling the pattern at run time.</summary>
        [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._@-]{0,63}$", RegexOptions.CultureInvariant)]
        private static partial Regex ValidUserName();

        private readonly IUserStore _store;
        private readonly ITenancyStore _tenancy;
        private readonly SecurityOptions _options;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly PasswordHasher<UserRecord> _hasher = new PasswordHasher<UserRecord>();
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);

        #endregion

        #region Public Methods

        public Task<UserRecord> FindAsync(string id, CancellationToken cancellationToken = default) => _store.FindByIdAsync(id, cancellationToken);

        public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => _store.AnyAsync(cancellationToken);

        /// <summary>The user's membership the session acts in: their first organization.</summary>
        public async Task<MembershipRecord> FindHomeMembershipAsync(string userId, CancellationToken cancellationToken = default)
        {
            return (await _store.GetMembershipsAsync(userId, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        }

        public async Task<IReadOnlyList<Member>> GetMembersAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            var members = new List<Member>();
            foreach (var membership in await _store.GetMembersAsync(organizationId, cancellationToken).ConfigureAwait(false))
            {
                var user = await _store.FindByIdAsync(membership.UserId, cancellationToken).ConfigureAwait(false);
                if (user != null)
                {
                    members.Add(new Member(user, membership));
                }
            }

            return members.OrderBy(m => m.User.NormalizedUserName, StringComparer.Ordinal).ToList();
        }

        public async Task<Member> FindMemberAsync(string organizationId, string userId, CancellationToken cancellationToken = default)
        {
            var membership = await _store.FindMembershipAsync(userId, organizationId, cancellationToken).ConfigureAwait(false);
            var user = membership == null ? null : await _store.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
            return user == null ? null : new Member(user, membership);
        }

        /// <summary>The member when the credentials are right and the account is enabled, not locked and belongs to an organization; otherwise null.</summary>
        public async Task<Member> SignInAsync(string userName, string password, CancellationToken cancellationToken = default)
        {
            var user = await _store.FindByNameAsync(userName, cancellationToken).ConfigureAwait(false);
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            if (user == null || string.IsNullOrEmpty(password))
            {
                _hasher.VerifyHashedPassword(null, DummyHash(), password ?? string.Empty);
                return null;
            }

            if (!user.Enabled || (user.LockedUntil.HasValue && user.LockedUntil.Value > now))
            {
                _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
                return null;
            }

            var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);

            if (result == PasswordVerificationResult.Failed)
            {
                user.FailedLogins++;

                if (user.FailedLogins >= _options.MaxFailedLogins)
                {
                    user.FailedLogins = 0;
                    user.LockedUntil = now + _options.LockoutDuration;
                    _logger.LogWarning("User {UserName} locked for {Duration} after repeated failed sign-ins", user.UserName, _options.LockoutDuration);
                }

                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
                return null;
            }

            var membership = await FindHomeMembershipAsync(user.Id, cancellationToken).ConfigureAwait(false);
            if (membership == null)
            {
                return null;
            }

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _hasher.HashPassword(user, password);
            }

            user.FailedLogins = 0;
            user.LockedUntil = null;
            user.LastLoginAt = now;
            await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
            return new Member(user, membership);
        }

        /// <summary>
        /// Adds a user to the organization. <paramref name="actorRole"/> is the role of whoever asks: only owners make owners.
        /// </summary>
        public async Task<UserResult> CreateMemberAsync(string organizationId, string actorRole, string userName, string password, string role, IReadOnlyList<string> sites, bool mustChangePassword = true, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var errors = new List<string>();
                var canonicalRole = Roles.Normalize(role);

                if (userName == null || !ValidUserName().IsMatch(userName.Trim()))
                {
                    errors.Add("User names are 1-64 letters, digits, '.', '_', '@' or '-', starting with a letter or digit.");
                }

                if (canonicalRole == null)
                {
                    errors.Add($"Role must be one of {string.Join(", ", Roles.All)}.");
                }
                else if (canonicalRole == Roles.Owner && actorRole != Roles.Owner)
                {
                    return UserResult.Forbidden("Only owners can add owners.");
                }

                errors.AddRange(CheckPassword(password));
                errors.AddRange(await CheckSitesAsync(organizationId, sites, cancellationToken).ConfigureAwait(false));

                if (errors.Count > 0)
                {
                    return UserResult.Invalid(errors);
                }

                if (await _store.FindByNameAsync(userName, cancellationToken).ConfigureAwait(false) != null)
                {
                    return UserResult.Conflict($"User '{userName.Trim()}' already exists.");
                }

                var now = _timeProvider.GetUtcNow().UtcDateTime;
                var user = new UserRecord
                {
                    Id = Guid.NewGuid().ToString("D"),
                    UserName = userName.Trim(),
                    Enabled = true,
                    MustChangePassword = mustChangePassword,
                    SecurityStamp = NewStamp(),
                    CreatedAt = now
                };
                user.PasswordHash = _hasher.HashPassword(user, password);

                var membership = new MembershipRecord
                {
                    Id = Guid.NewGuid().ToString("D"),
                    UserId = user.Id,
                    OrganizationId = organizationId,
                    Role = canonicalRole,
                    Sites = WriteSites(sites),
                    CreatedAt = now
                };

                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
                await _store.SaveMembershipAsync(membership, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("User {UserName} added as {Role}", user.UserName, membership.Role);
                return UserResult.Ok(new Member(user, membership));
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>
        /// Changes role, sites, state or password (null: unchanged); a password set this way must be changed at the next
        /// sign-in. Only owners change owners or make them.
        /// </summary>
        public async Task<UserResult> UpdateMemberAsync(string organizationId, string actorRole, string userId, string role, IReadOnlyList<string> sites, bool? enabled, string password, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var member = await FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
                if (member == null)
                {
                    return UserResult.NotFound();
                }

                var (user, membership) = (member.User, member.Membership);
                var canonicalRole = role == null ? membership.Role : Roles.Normalize(role);
                if (canonicalRole == null)
                {
                    return UserResult.Invalid(new[] { $"Role must be one of {string.Join(", ", Roles.All)}." });
                }

                if ((membership.Role == Roles.Owner || canonicalRole == Roles.Owner) && actorRole != Roles.Owner)
                {
                    return UserResult.Forbidden("Only owners change owners.");
                }

                var errors = password == null ? new List<string>() : CheckPassword(password);
                if (sites != null)
                {
                    errors.AddRange(await CheckSitesAsync(organizationId, sites, cancellationToken).ConfigureAwait(false));
                }

                if (errors.Count > 0)
                {
                    return UserResult.Invalid(errors);
                }

                var newEnabled = enabled ?? user.Enabled;
                var newSites = sites == null ? membership.Sites : WriteSites(sites);

                if (membership.Role == Roles.Owner && user.Enabled && (canonicalRole != Roles.Owner || !newEnabled || newSites != WriteSites(null))
                    && await IsLastOwnerAsync(organizationId, user.Id, cancellationToken).ConfigureAwait(false))
                {
                    return UserResult.Conflict("At least one enabled owner with access to all sites is required.");
                }

                var changed = canonicalRole != membership.Role || newSites != membership.Sites || newEnabled != user.Enabled || password != null;
                membership.Role = canonicalRole;
                membership.Sites = newSites;
                user.Enabled = newEnabled;

                if (password != null)
                {
                    user.PasswordHash = _hasher.HashPassword(user, password);
                    user.MustChangePassword = true;
                    user.FailedLogins = 0;
                    user.LockedUntil = null;
                }

                if (changed)
                {
                    user.SecurityStamp = NewStamp();
                }

                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
                await _store.SaveMembershipAsync(membership, cancellationToken).ConfigureAwait(false);
                return UserResult.Ok(new Member(user, membership));
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Removes the user from the organization (and deletes the account, which belongs to no other).</summary>
        public async Task<UserResult> DeleteMemberAsync(string organizationId, string actorRole, string userId, string currentUserId, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var member = await FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
                if (member == null)
                {
                    return UserResult.NotFound();
                }

                if (string.Equals(userId, currentUserId, StringComparison.Ordinal))
                {
                    return UserResult.Conflict("You cannot delete your own account.");
                }

                if (member.Membership.Role == Roles.Owner && actorRole != Roles.Owner)
                {
                    return UserResult.Forbidden("Only owners remove owners.");
                }

                if (member.Membership.Role == Roles.Owner && member.User.Enabled && await IsLastOwnerAsync(organizationId, userId, cancellationToken).ConfigureAwait(false))
                {
                    return UserResult.Conflict("At least one enabled owner with access to all sites is required.");
                }

                await _store.DeleteAsync(userId, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("User {UserName} deleted", member.User.UserName);
                return UserResult.Ok(member);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>The user's own password change; ends the user's other sessions.</summary>
        public async Task<UserResult> ChangePasswordAsync(string id, string currentPassword, string newPassword, CancellationToken cancellationToken = default)
        {
            var user = await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
            var membership = user == null ? null : await FindHomeMembershipAsync(user.Id, cancellationToken).ConfigureAwait(false);
            if (membership == null)
            {
                return UserResult.NotFound();
            }

            if (_hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword ?? string.Empty) == PasswordVerificationResult.Failed)
            {
                return UserResult.Invalid(new[] { "The current password is wrong." });
            }

            var errors = CheckPassword(newPassword);
            if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            {
                errors.Add("The new password must differ from the current one.");
            }

            if (errors.Count > 0)
            {
                return UserResult.Invalid(errors);
            }

            user.PasswordHash = _hasher.HashPassword(user, newPassword);
            user.MustChangePassword = false;
            user.SecurityStamp = NewStamp();
            await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
            return UserResult.Ok(new Member(user, membership));
        }

        /// <summary>The identity carried by the member's tokens: user, role and sites in the organization.</summary>
        public static ClaimsPrincipal CreatePrincipal(Member member, string authenticationScheme)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, member.User.Id),
                new Claim(ClaimTypes.Name, member.User.UserName),
                new Claim(ClaimTypes.Role, member.Membership.Role),
                new Claim(RecorderClaims.Organization, member.Membership.OrganizationId),
                new Claim(RecorderClaims.SecurityStamp, member.User.SecurityStamp)
            };

            if (member.Sites.Count > 0)
            {
                claims.Add(new Claim(RecorderClaims.Sites, string.Join(' ', member.Sites)));
            }

            if (member.User.MustChangePassword)
            {
                claims.Add(new Claim(RecorderClaims.MustChangePassword, "true"));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
        }

        public static IReadOnlyList<string> ReadSites(string json)
        {
            return string.IsNullOrEmpty(json) ? Array.Empty<string>() : JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }

        public static IReadOnlyList<string> CheckPasswordRules(string password) => CheckPassword(password);

        #endregion

        #region Private Methods

        private static string WriteSites(IReadOnlyList<string> sites)
        {
            return JsonSerializer.Serialize((sites ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
        }

        private async Task<List<string>> CheckSitesAsync(string organizationId, IReadOnlyList<string> sites, CancellationToken cancellationToken)
        {
            var errors = new List<string>();
            if (sites == null || sites.Count == 0)
            {
                return errors;
            }

            var known = (await _tenancy.GetSitesAsync(organizationId, cancellationToken).ConfigureAwait(false)).Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var site in sites.Where(s => !known.Contains(s)).Distinct())
            {
                errors.Add($"Site '{site}' does not exist.");
            }

            return errors;
        }

        /// <summary>Hashed once, so unknown user names take as long as wrong passwords.</summary>
        private string DummyHash()
        {
            return _dummyHash ??= _hasher.HashPassword(new UserRecord(), Guid.NewGuid().ToString("N"));
        }

        private string _dummyHash;

        private async Task<bool> IsLastOwnerAsync(string organizationId, string userId, CancellationToken cancellationToken)
        {
            foreach (var other in await GetMembersAsync(organizationId, cancellationToken).ConfigureAwait(false))
            {
                if (other.User.Id != userId && other.Membership.Role == Roles.Owner && other.User.Enabled && other.Sites.Count == 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<string> CheckPassword(string password)
        {
            var errors = new List<string>();

            if (password == null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
            {
                errors.Add($"Passwords need {MinPasswordLength} to {MaxPasswordLength} characters.");
            }
            else if (password.Distinct().Count() < 4)
            {
                errors.Add("The password is too simple.");
            }

            return errors;
        }

        private static string NewStamp()
        {
            return Guid.NewGuid().ToString("N");
        }

        #endregion
    }

    public sealed class UserResult
    {
        private UserResult(UserStatus status, Member member, IReadOnlyList<string> errors)
        {
            Status = status;
            Member = member;
            Errors = errors ?? Array.Empty<string>();
        }

        public UserStatus Status { get; }
        public Member Member { get; }
        public IReadOnlyList<string> Errors { get; }

        public static UserResult Ok(Member member) => new UserResult(UserStatus.Ok, member, null);
        public static UserResult Forbidden(string error) => new UserResult(UserStatus.Forbidden, null, new[] { error });
        public static UserResult NotFound() => new UserResult(UserStatus.NotFound, null, null);
        public static UserResult Conflict(string error) => new UserResult(UserStatus.Conflict, null, new[] { error });
        public static UserResult Invalid(IEnumerable<string> errors) => new UserResult(UserStatus.Invalid, null, errors.ToList());
    }

    public enum UserStatus
    {
        Ok,
        NotFound,
        Conflict,
        Invalid,
        Forbidden
    }

    /// <summary>
    /// "Security" section.
    /// </summary>
    public sealed class SecurityOptions
    {
        public const string SectionName = "Security";

        /// <summary>
        /// For automated installs: the owner created when there are no users, instead of the first-run setup
        /// (with a default organization and site). Without a password, setup is done in the web application.
        /// </summary>
        public InitialAdminOptions InitialAdmin { get; set; } = new InitialAdminOptions();

        /// <summary>When set, the first-run setup also asks for this token (for recorders reachable before setup is done).</summary>
        public string SetupToken { get; set; }

        /// <summary>Origins of the web application(s) allowed to call the API from a browser (CORS), e.g. "https://cameras.example.com".</summary>
        public List<string> AllowedOrigins { get; set; } = new List<string>();

        public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
        public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

        /// <summary>Failed sign-ins in a row before the account is locked for <see cref="LockoutDuration"/>.</summary>
        public int MaxFailedLogins { get; set; } = 5;

        public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>Sign-in attempts per client address and minute.</summary>
        public int SignInsPerMinute { get; set; } = 10;
    }

    public sealed class InitialAdminOptions
    {
        public string UserName { get; set; } = "admin";
        public string Password { get; set; }
        public string OrganizationName { get; set; }

        /// <summary>Data region: "asia" or "us" (default).</summary>
        public string Region { get; set; }

        public string SiteName { get; set; }

        /// <summary>IANA time zone of the first site; default UTC.</summary>
        public string TimeZone { get; set; }
    }
}
