// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>Gateway tokens: random, shown once, stored only as a SHA-256 hash.</summary>
    public static class GatewayTokens
    {
        public static string Create() => Base64Url(RandomNumberGenerator.GetBytes(32));

        public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty))).ToLowerInvariant();

        public static bool Same(string a, string b) =>
            a != null && b != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>The state of a pairing as the waiting gateway sees it.</summary>
    public sealed record PairingStatus(string State, DateTime ExpiresAt, string GatewayId = null, string Token = null)
    {
        public const string Waiting = "Waiting";
        public const string Paired = "Paired";
        public const string Expired = "Expired";

        /// <summary>A pairing code's life on the control plane (in memory; gone once paired or expired).</summary>
        public static Onvif.Open.Core.StateMachines.StateMachine<string> Machine { get; } = new Onvif.Open.Core.StateMachines.StateMachine<string>(
            "Gateway pairing",
            Waiting,
            (Waiting, Paired, "an administrator entered the code"),
            (Waiting, Expired, "10 minutes passed"));
    }

    /// <summary>What a gateway gets when it asks to be paired: the code to show, and how to ask whether it was entered.</summary>
    public sealed record PairingTicket(string Id, string Code, string Secret, DateTime ExpiresAt);

    /// <summary>
    /// Pairs gateways on other machines with a 6-digit code: the gateway asks for a code and shows it, an administrator
    /// enters it here, then the gateway collects its token (once). Codes are valid for <see cref="Lifetime"/>; pending
    /// pairings are kept in memory (a gateway asks again after a restart of this server).
    /// </summary>
    public sealed class GatewayPairing
    {
        #region Constructor

        public GatewayPairing(GatewayService gateways, TimeProvider timeProvider)
        {
            _gateways = gateways ?? throw new ArgumentNullException(nameof(gateways));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Public Variables

        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        /// <summary>At most this many gateways wait at once (a flood of requests cannot use up the codes).</summary>
        public const int MaxPending = 1000;

        #endregion

        #region Private Variables

        private sealed class Pending
        {
            public string Id { get; init; }
            public string Code { get; init; }
            public string SecretHash { get; init; }
            public string Name { get; init; }
            public string Location { get; init; }
            public DateTime ExpiresAt { get; init; }
            public string GatewayId { get; set; }
            public string Token { get; set; }
        }

        private readonly GatewayService _gateways;
        private readonly TimeProvider _timeProvider;
        private readonly ConcurrentDictionary<string, Pending> _pending = new ConcurrentDictionary<string, Pending>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _pairing = new SemaphoreSlim(1, 1);

        #endregion

        #region Public Methods

        /// <summary>A new code for a gateway that wants to be paired; null when too many are waiting.</summary>
        public PairingTicket Start(string name, string location)
        {
            RemoveExpired();
            if (_pending.Count >= MaxPending)
            {
                return null;
            }

            var secret = GatewayTokens.Create();
            var expires = Now + Lifetime;
            while (true)
            {
                var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                if (_pending.Values.Any(p => p.Code == code))
                {
                    continue;
                }

                var pending = new Pending
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Code = code,
                    SecretHash = GatewayTokens.Hash(secret),
                    Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                    Location = GatewayLocations.Normalize(location),
                    ExpiresAt = expires
                };
                _pending[pending.Id] = pending;
                return new PairingTicket(pending.Id, code, secret, expires);
            }
        }

        /// <summary>
        /// Whether the code was entered; once paired, the token is handed over this one time. Null: unknown pairing or
        /// wrong secret.
        /// </summary>
        public PairingStatus Poll(string id, string secret)
        {
            if (id == null || !_pending.TryGetValue(id, out var pending) || !GatewayTokens.Same(pending.SecretHash, GatewayTokens.Hash(secret)))
            {
                return null;
            }

            if (pending.Token != null)
            {
                _pending.TryRemove(id, out _);
                return new PairingStatus(PairingStatus.Paired, pending.ExpiresAt, pending.GatewayId, pending.Token);
            }

            if (Now >= pending.ExpiresAt)
            {
                _pending.TryRemove(id, out _);
                return new PairingStatus(PairingStatus.Expired, pending.ExpiresAt);
            }

            return new PairingStatus(PairingStatus.Waiting, pending.ExpiresAt);
        }

        /// <summary>An administrator entered a code: the waiting gateway becomes one of the organization's.</summary>
        public async Task<ServiceResult<Gateway>> PairAsync(string organizationId, string code, string name, CancellationToken cancellationToken = default)
        {
            var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
            if (digits.Length != 6)
            {
                return ServiceResult<Gateway>.Invalid(new[] { "Enter the 6-digit code the gateway shows." });
            }

            if (name != null && (string.IsNullOrWhiteSpace(name) || name.Trim().Length > GatewayService.MaxNameLength))
            {
                return ServiceResult<Gateway>.Invalid(new[] { $"Name is required and at most {GatewayService.MaxNameLength} characters." });
            }

            await _pairing.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var pending = _pending.Values.FirstOrDefault(p => p.Code == digits && p.Token == null && Now < p.ExpiresAt);
                if (pending == null)
                {
                    return ServiceResult<Gateway>.Invalid(new[] { "No gateway shows this code: check it, or restart the gateway for a new one (codes are valid for 10 minutes)." });
                }

                var (gateway, token) = await _gateways.CreatePairedAsync(organizationId, name ?? pending.Name, pending.Location, cancellationToken).ConfigureAwait(false);
                pending.GatewayId = gateway.Id;
                pending.Token = token;
                return ServiceResult<Gateway>.Ok(gateway);
            }
            finally
            {
                _pairing.Release();
            }
        }

        #endregion

        #region Private Methods

        private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

        private void RemoveExpired()
        {
            var now = Now;
            foreach (var expired in _pending.Values.Where(p => now >= p.ExpiresAt + Lifetime).ToList())
            {
                _pending.TryRemove(expired.Id, out _);
            }
        }

        #endregion
    }
}
