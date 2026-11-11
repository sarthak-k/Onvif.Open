// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Security
{
    /// <summary>A user's second factor (stored as <see cref="UserRecord.TwoFactorState"/>).</summary>
    public static class TwoFactorStates
    {
        public const string Off = "Off";
        public const string Pending = "Pending";
        public const string On = "On";

        public static Onvif.Open.Core.StateMachines.StateMachine<string> Machine { get; } = new Onvif.Open.Core.StateMachines.StateMachine<string>(
            "Two-factor sign-in",
            Off,
            (Off, Pending, "set up: the app got its secret"),
            (Pending, On, "a code from the app was entered"),
            (Pending, Off, "an administrator reset it"),
            (On, Off, "turned off, or reset by an administrator"));

        public static string Of(UserRecord user) => user?.TwoFactorState ?? Off;
    }

    /// <summary>What an authenticator app is set up with: scanned (<see cref="Uri"/>) or typed (<see cref="Secret"/>).</summary>
    public sealed record TwoFactorSetup(string Secret, string Uri);

    /// <summary>
    /// Two-factor sign-in with an authenticator app (TOTP): set up with a scanned secret and confirmed with a code; ten
    /// one-time recovery codes for a lost phone; an administrator can reset it. After the password, sign-in asks for a
    /// code with a short-lived challenge, and wrong codes count towards the lockout like wrong passwords.
    /// </summary>
    public sealed class TwoFactorService
    {
        #region Constructor

        public TwoFactorService(IUserStore store, ITenancyStore tenancy, UserService users, IDataProtectionProvider dataProtection, ILogger<TwoFactorService> logger, TimeProvider timeProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _tenancy = tenancy ?? throw new ArgumentNullException(nameof(tenancy));
            _users = users ?? throw new ArgumentNullException(nameof(users));
            var protection = dataProtection ?? throw new ArgumentNullException(nameof(dataProtection));
            _secrets = protection.CreateProtector("Onvif.Open.Recorder.TwoFactorSecret.v1");
            _challenges = protection.CreateProtector("Onvif.Open.Recorder.TwoFactorChallenge.v1");
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Public Variables

        public const string Issuer = "Onvif.Open";
        public const int RecoveryCodeCount = 10;

        /// <summary>How long the code may take after the password.</summary>
        public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

        #endregion

        #region Private Variables

        private readonly IUserStore _store;
        private readonly ITenancyStore _tenancy;
        private readonly UserService _users;
        private readonly IDataProtector _secrets;
        private readonly IDataProtector _challenges;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);

        #endregion

        #region Public Methods - the user's own

        /// <summary>A new secret for the user's app; two-factor sign-in starts once a code from it is entered.</summary>
        public async Task<ServiceResult<TwoFactorSetup>> BeginSetupAsync(string userId, CancellationToken cancellationToken = default)
        {
            return await ChangeAsync(userId, async user =>
            {
                var state = TwoFactorStates.Of(user);
                if (state == TwoFactorStates.On)
                {
                    return ServiceResult<TwoFactorSetup>.Conflict("Two-factor sign-in is on already: turn it off first to use another app.");
                }

                // Setting up again before confirming replaces the secret (another phone, a lost QR code).
                var secret = Totp.NewSecret();
                user.TwoFactorState = state == TwoFactorStates.Pending ? state : TwoFactorStates.Machine.Move(state, TwoFactorStates.Pending);
                user.TwoFactorSecret = _secrets.Protect(Convert.ToBase64String(secret));
                user.TwoFactorLastStep = 0;
                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
                return ServiceResult<TwoFactorSetup>.Ok(new TwoFactorSetup(Totp.Base32(secret), Totp.Uri(Issuer, user.UserName, secret)));
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Turns it on with a code from the app; returns the recovery codes (shown once).</summary>
        public async Task<ServiceResult<IReadOnlyList<string>>> EnableAsync(string userId, string code, CancellationToken cancellationToken = default)
        {
            return await ChangeAsync(userId, async user =>
            {
                if (TwoFactorStates.Of(user) != TwoFactorStates.Pending)
                {
                    return ServiceResult<IReadOnlyList<string>>.Conflict(TwoFactorStates.Of(user) == TwoFactorStates.On ? "Two-factor sign-in is on already." : "Set up the app first.");
                }

                var step = Totp.Verify(SecretOf(user), code, _timeProvider.GetUtcNow(), user.TwoFactorLastStep);
                if (step == null)
                {
                    return ServiceResult<IReadOnlyList<string>>.Invalid(new[] { "That code is not right: enter the one the app shows now." });
                }

                user.TwoFactorState = TwoFactorStates.Machine.Move(TwoFactorStates.Pending, TwoFactorStates.On);
                user.TwoFactorLastStep = step.Value;
                var codes = NewRecoveryCodes(user);
                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Two-factor sign-in turned on for {UserName}", user.UserName);
                return ServiceResult<IReadOnlyList<string>>.Ok(codes);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Turns it off, with the password and a code (or a recovery code); not where the organization requires it.</summary>
        public async Task<ServiceResult<UserRecord>> DisableAsync(string userId, string password, string code, CancellationToken cancellationToken = default)
        {
            return await ChangeAsync(userId, async user =>
            {
                if (TwoFactorStates.Of(user) != TwoFactorStates.On)
                {
                    return ServiceResult<UserRecord>.Conflict("Two-factor sign-in is not on.");
                }

                var membership = await _users.FindHomeMembershipAsync(user.Id, cancellationToken).ConfigureAwait(false);
                if ((await _tenancy.FindOrganizationAsync(membership?.OrganizationId, cancellationToken).ConfigureAwait(false))?.RequireTwoFactor == true)
                {
                    return ServiceResult<UserRecord>.Forbidden("Your organization requires two-factor sign-in.");
                }

                if (!_users.IsPassword(user, password) || !Verify(user, code))
                {
                    return ServiceResult<UserRecord>.Invalid(new[] { "The password or the code is not right." });
                }

                TurnOff(user);
                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Two-factor sign-in turned off for {UserName}", user.UserName);
                return ServiceResult<UserRecord>.Ok(user);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Ten new recovery codes (the old ones stop working), with a code from the app.</summary>
        public async Task<ServiceResult<IReadOnlyList<string>>> NewRecoveryCodesAsync(string userId, string code, CancellationToken cancellationToken = default)
        {
            return await ChangeAsync(userId, async user =>
            {
                if (TwoFactorStates.Of(user) != TwoFactorStates.On)
                {
                    return ServiceResult<IReadOnlyList<string>>.Conflict("Two-factor sign-in is not on.");
                }

                var step = Totp.Verify(SecretOf(user), code, _timeProvider.GetUtcNow(), user.TwoFactorLastStep);
                if (step == null)
                {
                    return ServiceResult<IReadOnlyList<string>>.Invalid(new[] { "That code is not right: enter the one the app shows now." });
                }

                user.TwoFactorLastStep = step.Value;
                var codes = NewRecoveryCodes(user);
                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
                return ServiceResult<IReadOnlyList<string>>.Ok(codes);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>How many recovery codes are left.</summary>
        public static int RecoveryCodesLeft(UserRecord user) => ReadCodes(user).Count;

        #endregion

        #region Public Methods - administrators

        /// <summary>
        /// For a member who lost their phone and recovery codes: turns it off and ends their sessions (they set it up
        /// again at the next sign-in where the organization requires it). Only owners reset owners.
        /// </summary>
        public async Task<ServiceResult<UserRecord>> ResetAsync(string organizationId, string actorRole, string userId, CancellationToken cancellationToken = default)
        {
            var member = await _users.FindMemberAsync(organizationId, userId, cancellationToken).ConfigureAwait(false);
            if (member == null)
            {
                return ServiceResult<UserRecord>.NotFound();
            }

            if (member.Membership.Role == Roles.Owner && actorRole != Roles.Owner)
            {
                return ServiceResult<UserRecord>.Forbidden("Only owners reset owners.");
            }

            return await ChangeAsync(userId, async user =>
            {
                if (TwoFactorStates.Of(user) == TwoFactorStates.Off)
                {
                    return ServiceResult<UserRecord>.Conflict("Two-factor sign-in is not set up for this user.");
                }

                TurnOff(user);
                user.SecurityStamp = UserService.NewStamp();
                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Two-factor sign-in of {UserName} reset by an administrator", user.UserName);
                return ServiceResult<UserRecord>.Ok(user);
            }, cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Public Methods - sign-in

        /// <summary>Whether the password is not enough for this user.</summary>
        public static bool IsRequiredAtSignIn(UserRecord user) => TwoFactorStates.Of(user) == TwoFactorStates.On;

        /// <summary>What the client sends back with the code; it expires after <see cref="ChallengeLifetime"/>.</summary>
        public string CreateChallenge(UserRecord user) =>
            _challenges.Protect($"{user.Id}|{user.SecurityStamp}|{(_timeProvider.GetUtcNow() + ChallengeLifetime).ToUnixTimeSeconds()}");

        /// <summary>
        /// The member when the challenge is current and the code right (an app code once, or an unused recovery code);
        /// null otherwise. <paramref name="user"/> is the one the challenge was for (for the audit), when known.
        /// </summary>
        public async Task<(Member Member, UserRecord User)> CompleteSignInAsync(string challenge, string code, CancellationToken cancellationToken = default)
        {
            string payload;
            try
            {
                payload = string.IsNullOrEmpty(challenge) ? null : _challenges.Unprotect(challenge);
            }
            catch (CryptographicException)
            {
                payload = null;
            }

            // "user|stamp|expires (Unix seconds)", by this server's clock.
            var parts = payload?.Split('|');
            if (parts?.Length != 3 || !long.TryParse(parts[2], out var expires) || _timeProvider.GetUtcNow().ToUnixTimeSeconds() >= expires)
            {
                return (null, null);
            }

            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var user = await _store.FindByIdAsync(parts[0], cancellationToken).ConfigureAwait(false);
                if (user == null || !string.Equals(user.SecurityStamp, parts[1], StringComparison.Ordinal) || !IsRequiredAtSignIn(user) || !_users.CanSignIn(user))
                {
                    return (null, user);
                }

                if (!Verify(user, code))
                {
                    await _users.RecordFailedSignInAsync(user, cancellationToken).ConfigureAwait(false);
                    return (null, user);
                }

                user.FailedLogins = 0;
                user.LockedUntil = null;
                user.LastLoginAt = _timeProvider.GetUtcNow().UtcDateTime;
                await _store.SaveAsync(user, cancellationToken).ConfigureAwait(false);

                var membership = await _users.FindHomeMembershipAsync(user.Id, cancellationToken).ConfigureAwait(false);
                return (membership == null ? null : await _users.WithPolicyAsync(new Member(user, membership), cancellationToken).ConfigureAwait(false), user);
            }
            finally
            {
                _changes.Release();
            }
        }

        #endregion

        #region Private Methods

        private async Task<ServiceResult<T>> ChangeAsync<T>(string userId, Func<UserRecord, Task<ServiceResult<T>>> change, CancellationToken cancellationToken)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var user = userId == null ? null : await _store.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
                return user == null ? ServiceResult<T>.NotFound() : await change(user).ConfigureAwait(false);
            }
            finally
            {
                _changes.Release();
            }
        }

        private byte[] SecretOf(UserRecord user) => Convert.FromBase64String(_secrets.Unprotect(user.TwoFactorSecret));

        /// <summary>An app code (each works once) or an unused recovery code (used up); the caller saves the user.</summary>
        private bool Verify(UserRecord user, string code)
        {
            if (Totp.Normalize(code) != null)
            {
                var step = Totp.Verify(SecretOf(user), code, _timeProvider.GetUtcNow(), user.TwoFactorLastStep);
                if (step != null)
                {
                    user.TwoFactorLastStep = step.Value;
                    return true;
                }

                return false;
            }

            var codes = ReadCodes(user);
            var hash = HashCode(code);
            if (hash == null || !codes.Remove(hash))
            {
                return false;
            }

            user.RecoveryCodes = JsonSerializer.Serialize(codes);
            _logger?.LogInformation("{UserName} signed in with a recovery code ({Left} left)", user.UserName, codes.Count);
            return true;
        }

        private static void TurnOff(UserRecord user)
        {
            user.TwoFactorState = TwoFactorStates.Machine.Move(TwoFactorStates.Of(user), TwoFactorStates.Off);
            user.TwoFactorSecret = null;
            user.TwoFactorLastStep = 0;
            user.RecoveryCodes = null;
        }

        /// <summary>"abcd-efgh-ijkl": 60 random bits each, stored only as hashes.</summary>
        private static IReadOnlyList<string> NewRecoveryCodes(UserRecord user)
        {
            var codes = Enumerable.Range(0, RecoveryCodeCount)
                .Select(_ => Totp.Base32(RandomNumberGenerator.GetBytes(8)).Substring(0, 12).ToLowerInvariant())
                .Select(c => $"{c.Substring(0, 4)}-{c.Substring(4, 4)}-{c.Substring(8, 4)}")
                .ToList();
            user.RecoveryCodes = JsonSerializer.Serialize(codes.Select(HashCode).ToList());
            return codes;
        }

        private static List<string> ReadCodes(UserRecord user) =>
            string.IsNullOrEmpty(user?.RecoveryCodes) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(user.RecoveryCodes) ?? new List<string>();

        private static string HashCode(string code)
        {
            var normalized = new string((code ?? string.Empty).Where(char.IsAsciiLetterOrDigit).ToArray()).ToLowerInvariant();
            return normalized.Length != 12 ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        }

        #endregion
    }
}
