// Sarthak Killedar
// 2026-11-09

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>Where a paired gateway keeps its identity: its id and token at the control plane.</summary>
    public interface IControlPlaneCredentials
    {
        ControlPlaneIdentity Load();
        void Save(ControlPlaneIdentity identity);
        void Clear();
    }

    public sealed record ControlPlaneIdentity(string GatewayId, string Token);

    /// <summary>
    /// The identity in a file next to the recorder's data, the token encrypted with the recorder's Data Protection keys.
    /// </summary>
    public sealed class FileControlPlaneCredentials : IControlPlaneCredentials
    {
        public FileControlPlaneCredentials(string path, IDataProtectionProvider dataProtection)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _protector = (dataProtection ?? throw new ArgumentNullException(nameof(dataProtection))).CreateProtector("Onvif.Open.Recorder.ControlPlaneToken.v1");
        }

        private sealed record Stored(string GatewayId, string Token);

        private readonly string _path;
        private readonly IDataProtector _protector;

        public ControlPlaneIdentity Load()
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path));
            return stored?.Token == null ? null : new ControlPlaneIdentity(stored.GatewayId, _protector.Unprotect(stored.Token));
        }

        public void Save(ControlPlaneIdentity identity)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
            File.WriteAllText(_path, JsonSerializer.Serialize(new Stored(identity.GatewayId, _protector.Protect(identity.Token))));
        }

        public void Clear()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }

    /// <summary>
    /// Runs this recorder as a gateway of another one (the control plane, "ControlPlane:Url"): until it is paired it asks
    /// for a 6-digit code and writes it to the log for the installer to enter in the web app (Settings › Gateways › Add
    /// gateway); then it reports its health every <see cref="HeartbeatInterval"/>. A removed gateway pairs again.
    /// Only outbound HTTPS requests to the control plane are made.
    /// </summary>
    public sealed class ControlPlaneClient : BackgroundService
    {
        #region Constructor

        public ControlPlaneClient(HttpClient http, IControlPlaneCredentials credentials, Func<GatewayHealth> measure, string name, string location, ILogger<ControlPlaneClient> logger, TimeProvider timeProvider = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _measure = measure ?? throw new ArgumentNullException(nameof(measure));
            _name = name;
            _location = location;
            _logger = logger;
            _startedAt = (timeProvider ?? TimeProvider.System).GetUtcNow();
        }

        #endregion

        #region Public Variables

        public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
        public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(15);

        /// <summary>The code waiting to be entered, while there is one.</summary>
        public string PairingCode => _ticket?.Code;

        public bool IsPaired => _identity != null;

        public string GatewayId => _identity?.GatewayId;

        #endregion

        #region Private Variables

        private sealed record Ticket(string Id, string Code, string Secret, DateTimeOffset ExpiresAt);

        private sealed record PollAnswer(string State, string GatewayId, string Token);

        private readonly HttpClient _http;
        private readonly IControlPlaneCredentials _credentials;
        private readonly Func<GatewayHealth> _measure;
        private readonly string _name;
        private readonly string _location;
        private readonly ILogger _logger;
        private readonly DateTimeOffset _startedAt;
        private ControlPlaneIdentity _identity;
        private Ticket _ticket;
        private bool _loaded;

        #endregion

        #region Public Methods

        /// <summary>One step: report, ask for a code, or check whether it was entered. Returns how long to wait before the next.</summary>
        public async Task<TimeSpan> StepAsync(CancellationToken cancellationToken = default)
        {
            if (!_loaded)
            {
                _identity = _credentials.Load();
                _loaded = true;
            }

            if (_identity != null)
            {
                return await HeartbeatAsync(cancellationToken).ConfigureAwait(false);
            }

            return _ticket == null
                ? await RequestCodeAsync(cancellationToken).ConfigureAwait(false)
                : await PollAsync(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Protected Methods

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan wait;
                try
                {
                    wait = await StepAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is JsonException || ex is IOException)
                {
                    _logger?.LogWarning("Cannot reach the control plane at {Url}: {Message}", _http.BaseAddress, ex.Message);
                    wait = RetryInterval;
                }

                await Task.Delay(wait, stoppingToken).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private Methods

        private async Task<TimeSpan> RequestCodeAsync(CancellationToken cancellationToken)
        {
            var response = await _http.PostAsJsonAsync("api/gateways/pairing", new { name = _name, location = _location }, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("The control plane refused to pair this gateway ({Status})", (int)response.StatusCode);
                return RetryInterval;
            }

            _ticket = await response.Content.ReadFromJsonAsync<Ticket>(cancellationToken: cancellationToken).ConfigureAwait(false);
            _logger?.LogWarning(
                "Pair this gateway: in the web app open Settings › Gateways › Add gateway and enter the code {Code} (valid until {ExpiresAt:HH:mm} UTC)",
                $"{_ticket.Code.Substring(0, 3)} {_ticket.Code.Substring(3)}", _ticket.ExpiresAt.UtcDateTime);
            return PollInterval;
        }

        private async Task<TimeSpan> PollAsync(CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, $"api/gateways/pairing/{Uri.EscapeDataString(_ticket.Id)}"))
            {
                request.Headers.Add(Api.GatewayEndpoints.PairingSecretHeader, _ticket.Secret);
                var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // The control plane restarted and forgot the code: ask for a new one.
                    _ticket = null;
                    return TimeSpan.Zero;
                }

                response.EnsureSuccessStatusCode();
                var answer = await response.Content.ReadFromJsonAsync<PollAnswer>(cancellationToken: cancellationToken).ConfigureAwait(false);

                if (answer.State == PairingStatus.Paired && answer.Token != null)
                {
                    _identity = new ControlPlaneIdentity(answer.GatewayId, answer.Token);
                    _credentials.Save(_identity);
                    _ticket = null;
                    _logger?.LogInformation("Gateway paired as {GatewayId}", answer.GatewayId);
                    return TimeSpan.Zero;
                }

                if (answer.State == PairingStatus.Expired)
                {
                    _ticket = null;
                    return TimeSpan.Zero;
                }

                return PollInterval;
            }
        }

        private async Task<TimeSpan> HeartbeatAsync(CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, "api/gateways/heartbeat"))
            {
                request.Headers.Add(Api.GatewayEndpoints.GatewayTokenHeader, _identity.Token);
                request.Content = JsonContent.Create(new { version = GatewayService.Version, startedAt = _startedAt, health = _measure() });
                var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Removed at the control plane: forget the token and pair again.
                    _logger?.LogWarning("The control plane no longer knows this gateway: pairing again");
                    _credentials.Clear();
                    _identity = null;
                    return TimeSpan.Zero;
                }

                response.EnsureSuccessStatusCode();
                return HeartbeatInterval;
            }
        }

        #endregion
    }
}
