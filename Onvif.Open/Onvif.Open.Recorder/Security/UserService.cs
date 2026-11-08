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
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Security
{
    /// <summary>
    /// Users: sign-in with lockout, password rules, roles, and the guarantee that an enabled admin always remains.
    /// Every change to a user's access rotates its security stamp, which ends its sessions.
    /// </summary>
    public sealed partial class UserService
    {
        #region Constructor

        public UserService(IUserStore store, SecurityOptions options, ILogger<UserService> logger, TimeProvider timeProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
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
        private readonly SecurityOptions _options;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly PasswordHasher<UserRecord> _hasher = new PasswordHasher<UserRecord>();
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);

        #endregion

        #region Public Methods

        public Task<IReadOnlyList<UserRecord>> GetUsersAsync(CancellationToken cancellationToken = default) => _store.GetUsersAsync(cancellationToken);

        public Task<UserRecord> FindAsync(string id, CancellationToken cancellationToken = default) => _store.FindByIdAsync(id, cancellationToken);

        /// <summary>The user when the credentials are right and the account is enabled and not locked; otherwise null.</summary>
        public async Task<UserRecord> SignInAsync(string userName, string password, CancellationToken cancellationToken = default)
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

            if (result == PasswordVerificationResult.SuccessRehashNeeded)
            {
                user.PasswordHash = _hasher.HashPassword(user, password);
            }

            user.FailedLogins = 0;
            user.LockedUntil = null;
            user.LastLoginAt = now;
            await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
            return user;
        }

        public async Task<UserResult> CreateAsync(string userName, string password, string role, bool mustChangePassword = false, CancellationToken cancellationToken = default)
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

                errors.AddRange(CheckPassword(password));

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
                    Role = canonicalRole,
                    Enabled = true,
                    MustChangePassword = mustChangePassword,
                    SecurityStamp = NewStamp(),
                    CreatedAt = now
                };

                user.PasswordHash = _hasher.HashPassword(user, password);
                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("User {UserName} created with role {Role}", user.UserName, user.Role);
                return UserResult.Ok(user);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Changes role, state or password (null: unchanged); a password set this way must be changed at the next sign-in.</summary>
        public async Task<UserResult> UpdateAsync(string id, string role, bool? enabled, string password, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var user = await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
                if (user == null)
                {
                    return UserResult.NotFound();
                }

                var canonicalRole = role == null ? user.Role : Roles.Normalize(role);
                if (canonicalRole == null)
                {
                    return UserResult.Invalid(new[] { $"Role must be one of {string.Join(", ", Roles.All)}." });
                }

                if (password != null)
                {
                    var errors = CheckPassword(password);
                    if (errors.Count > 0)
                    {
                        return UserResult.Invalid(errors);
                    }
                }

                var newEnabled = enabled ?? user.Enabled;
                if (user.Role == Roles.Admin && user.Enabled && (canonicalRole != Roles.Admin || !newEnabled) && await IsLastAdminAsync(user, cancellationToken).ConfigureAwait(false))
                {
                    return UserResult.Conflict("At least one enabled admin is required.");
                }

                var changed = canonicalRole != user.Role || newEnabled != user.Enabled || password != null;
                user.Role = canonicalRole;
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
                return UserResult.Ok(user);
            }
            finally
            {
                _changes.Release();
            }
        }

        public async Task<UserResult> DeleteAsync(string id, string currentUserId, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var user = await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
                if (user == null)
                {
                    return UserResult.NotFound();
                }

                if (string.Equals(user.Id, currentUserId, StringComparison.Ordinal))
                {
                    return UserResult.Conflict("You cannot delete your own account.");
                }

                if (user.Role == Roles.Admin && user.Enabled && await IsLastAdminAsync(user, cancellationToken).ConfigureAwait(false))
                {
                    return UserResult.Conflict("At least one enabled admin is required.");
                }

                await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("User {UserName} deleted", user.UserName);
                return UserResult.Ok(user);
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
            if (user == null)
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
            return UserResult.Ok(user);
        }

        /// <summary>
        /// Without users, creates the first admin: with the configured password, or a generated one that is logged once
        /// and must be changed at the first sign-in.
        /// </summary>
        public async Task EnsureInitialAdminAsync(CancellationToken cancellationToken = default)
        {
            if ((await _store.GetUsersAsync(cancellationToken).ConfigureAwait(false)).Count > 0)
            {
                return;
            }

            var configured = _options.InitialAdmin?.Password;
            var password = string.IsNullOrEmpty(configured) ? GeneratePassword() : configured;
            var result = await CreateAsync(_options.InitialAdmin?.UserName ?? "admin", password, Roles.Admin, mustChangePassword: string.IsNullOrEmpty(configured), cancellationToken).ConfigureAwait(false);

            if (result.Status != UserStatus.Ok)
            {
                throw new InvalidOperationException("Could not create the first admin: " + string.Join(" ", result.Errors));
            }

            if (string.IsNullOrEmpty(configured))
            {
                _logger.LogWarning("Created user {UserName} with the generated password {Password}: sign in and change it now (it is shown only once)", result.User.UserName, password);
            }
            else
            {
                _logger.LogInformation("Created user {UserName} with the password from Security:InitialAdmin", result.User.UserName);
            }
        }

        /// <summary>The identity carried by the user's tokens.</summary>
        public static ClaimsPrincipal CreatePrincipal(UserRecord user, string authenticationScheme)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim(RecorderClaims.SecurityStamp, user.SecurityStamp)
            };

            if (user.MustChangePassword)
            {
                claims.Add(new Claim(RecorderClaims.MustChangePassword, "true"));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationScheme, ClaimTypes.Name, ClaimTypes.Role));
        }

        #endregion

        #region Private Methods

        /// <summary>Hashed once, so unknown user names take as long as wrong passwords.</summary>
        private string DummyHash()
        {
            return _dummyHash ??= _hasher.HashPassword(new UserRecord(), Guid.NewGuid().ToString("N"));
        }

        private string _dummyHash;

        private async Task<bool> IsLastAdminAsync(UserRecord user, CancellationToken cancellationToken)
        {
            var users = await _store.GetUsersAsync(cancellationToken).ConfigureAwait(false);
            return !users.Any(u => u.Id != user.Id && u.Role == Roles.Admin && u.Enabled);
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

        private static string GeneratePassword()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            return new string(Enumerable.Range(0, 20).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        }

        #endregion
    }

    public sealed class UserResult
    {
        private UserResult(UserStatus status, UserRecord user, IReadOnlyList<string> errors)
        {
            Status = status;
            User = user;
            Errors = errors ?? Array.Empty<string>();
        }

        public UserStatus Status { get; }
        public UserRecord User { get; }
        public IReadOnlyList<string> Errors { get; }

        public static UserResult Ok(UserRecord user) => new UserResult(UserStatus.Ok, user, null);
        public static UserResult NotFound() => new UserResult(UserStatus.NotFound, null, null);
        public static UserResult Conflict(string error) => new UserResult(UserStatus.Conflict, null, new[] { error });
        public static UserResult Invalid(IEnumerable<string> errors) => new UserResult(UserStatus.Invalid, null, errors.ToList());
    }

    public enum UserStatus
    {
        Ok,
        NotFound,
        Conflict,
        Invalid
    }

    /// <summary>
    /// "Security" section.
    /// </summary>
    public sealed class SecurityOptions
    {
        public const string SectionName = "Security";

        /// <summary>The first admin, created when there are no users. Without a password, one is generated and logged once.</summary>
        public InitialAdminOptions InitialAdmin { get; set; } = new InitialAdminOptions();

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
    }
}
