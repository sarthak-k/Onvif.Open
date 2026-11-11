// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
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
    /// An identity given in the configuration ("ControlPlane:GatewayId" and "ControlPlane:Token", e.g. by the control
    /// plane that started this gateway): used as is, never replaced.
    /// </summary>
    public sealed class ConfiguredControlPlaneCredentials : IControlPlaneCredentials
    {
        public ConfiguredControlPlaneCredentials(string gatewayId, string token)
        {
            _identity = new ControlPlaneIdentity(gatewayId ?? throw new ArgumentNullException(nameof(gatewayId)), token ?? throw new ArgumentNullException(nameof(token)));
        }

        private readonly ControlPlaneIdentity _identity;

        public ControlPlaneIdentity Load() => _identity;

        public void Save(ControlPlaneIdentity identity)
        {
        }

        public void Clear()
        {
        }
    }

    /// <summary>
    /// Runs this recorder as a gateway of another one (the control plane, "ControlPlane:Url"): until it is paired it asks
    /// for a 6-digit code and writes it to the log for the installer to enter in the web app (Settings › Gateways › Add
    /// gateway); then it reports its health every <see cref="HeartbeatInterval"/>. A removed gateway pairs again.
    /// Only outbound HTTPS requests to the control plane are made.
    /// </summary>
    /// <summary>
    /// How a gateway reaches the control plane: HTTPS for pairing and heartbeats, and the tunnel (a WebSocket the
    /// gateway opens with its token). Tests connect two in-memory recorders through it.
    /// </summary>
    public sealed class ControlPlaneTransport
    {
        public ControlPlaneTransport(Func<HttpMessageHandler> handler, Func<Uri, string, CancellationToken, Task<System.Net.WebSockets.WebSocket>> connect)
        {
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Connect = connect ?? throw new ArgumentNullException(nameof(connect));
        }

        public Func<HttpMessageHandler> Handler { get; }

        /// <summary>Opens the tunnel: the WebSocket URL and the gateway's token.</summary>
        public Func<Uri, string, CancellationToken, Task<System.Net.WebSockets.WebSocket>> Connect { get; }

        /// <summary>Over the network (connections recycled every 5 minutes, so DNS changes are followed).</summary>
        public static ControlPlaneTransport Network { get; } = new ControlPlaneTransport(
            () => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) },
            async (uri, token, ct) =>
            {
                var socket = new System.Net.WebSockets.ClientWebSocket();
                socket.Options.SetRequestHeader(Api.GatewayEndpoints.GatewayTokenHeader, token);
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                await socket.ConnectAsync(uri, ct).ConfigureAwait(false);
                return socket;
            });
    }

    public static class TunnelStates
    {
        public const string Unpaired = "Unpaired";
        public const string Connecting = "Connecting";
        public const string Open = "Open";
        public const string Retrying = "Retrying";

        /// <summary>The gateway's tunnel to the control plane (docs/STATE-MACHINES.md).</summary>
        public static Onvif.Open.Core.StateMachines.StateMachine<string> Machine { get; } = new Onvif.Open.Core.StateMachines.StateMachine<string>(
            "Gateway tunnel",
            Unpaired,
            (Unpaired, Connecting, "paired: the gateway has its token"),
            (Connecting, Open, "the control plane accepted the WebSocket"),
            (Connecting, Retrying, "could not connect"),
            (Open, Retrying, "the tunnel closed"),
            (Retrying, Connecting, "after 1 s, doubling up to 30 s"),
            (Retrying, Unpaired, "removed at the control plane: pairing again"));
    }

    public sealed class ControlPlaneClient : BackgroundService
    {
        #region Constructor

        /// <param name="cameras">How the cameras the control plane assigned are doing (sent with each heartbeat).</param>
        /// <param name="assigned">The cameras the control plane has this gateway record (kept on disk, applied at startup).</param>
        /// <param name="tunnel">Answers the requests that come through the tunnel; null: no tunnel.</param>
        public ControlPlaneClient(HttpClient http, IControlPlaneCredentials credentials, Func<GatewayHealth> measure, string name, string location, ILogger<ControlPlaneClient> logger, TimeProvider timeProvider = null,
            ControlPlaneTransport transport = null, Func<IReadOnlyList<Tunnel.RemoteCameraStatus>> cameras = null, AssignedCameras assigned = null, Tunnel.TunnelExecutor tunnel = null, EventOutbox events = null)
        {
            _events = events;
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            _measure = measure ?? throw new ArgumentNullException(nameof(measure));
            _name = name;
            _location = location;
            _logger = logger;
            _startedAt = (timeProvider ?? TimeProvider.System).GetUtcNow();
            _transport = transport ?? ControlPlaneTransport.Network;
            _cameras = cameras ?? (() => Array.Empty<Tunnel.RemoteCameraStatus>());
            _assigned = assigned;
            _tunnel = tunnel;
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

        /// <summary>Whether the tunnel to the control plane is open.</summary>
        public bool IsConnected => _connection?.IsOpen == true;

        /// <summary>See <see cref="TunnelStates"/>.</summary>
        public string TunnelState { get; private set; } = TunnelStates.Unpaired;

        private void MoveTunnel(string to)
        {
            if (TunnelState != to)
            {
                TunnelState = TunnelStates.Machine.Move(TunnelState, to);
            }
        }

        public static readonly TimeSpan UploadInterval = TimeSpan.FromSeconds(2);
        public const int UploadBatch = 500;

        public static readonly TimeSpan TunnelRetry = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan TunnelMaxRetry = TimeSpan.FromSeconds(30);

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
        private readonly ControlPlaneTransport _transport;
        private readonly Func<IReadOnlyList<Tunnel.RemoteCameraStatus>> _cameras;
        private readonly AssignedCameras _assigned;
        private readonly Tunnel.TunnelExecutor _tunnel;
        private readonly EventOutbox _events;
        private volatile Tunnel.TunnelConnection _connection;
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
            // Record what the control plane assigned last time, before it can be reached again.
            if (_assigned != null)
            {
                await _assigned.LoadAsync(stoppingToken).ConfigureAwait(false);
            }

            await Task.WhenAll(
                ReportLoopAsync(stoppingToken),
                _tunnel == null ? Task.CompletedTask : TunnelLoopAsync(stoppingToken),
                _events == null ? Task.CompletedTask : UploadLoopAsync(stoppingToken)).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends the control plane the events waiting in the outbox, oldest first, in batches; each batch leaves the outbox
        /// once stored there. Returns how many were sent (also used by tests).
        /// </summary>
        public async Task<int> UploadEventsAsync(CancellationToken cancellationToken = default)
        {
            var identity = _identity;
            var sent = 0;
            while (identity != null && _events != null)
            {
                var batch = _events.Peek(UploadBatch);
                if (batch.Count == 0)
                {
                    break;
                }

                using (var request = new HttpRequestMessage(HttpMethod.Post, "api/gateways/events"))
                {
                    request.Headers.Add(Api.GatewayEndpoints.GatewayTokenHeader, identity.Token);
                    request.Content = JsonContent.Create(new { events = batch });
                    var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger?.LogWarning("The control plane did not take {Count} event change(s) ({Status}): keeping them", batch.Count, (int)response.StatusCode);
                        break;
                    }
                }

                _events.Remove(batch.Count);
                sent += batch.Count;
            }

            return sent;
        }

        private async Task UploadLoopAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await UploadEventsAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is IOException)
                {
                    _logger?.LogDebug("Events wait for the control plane: {Message}", ex.Message);
                }

                try
                {
                    await Task.Delay(UploadInterval, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>Keeps the tunnel open while paired: reconnects (1 s, doubling up to 30 s) whenever it closes.</summary>
        private async Task TunnelLoopAsync(CancellationToken stoppingToken)
        {
            var delay = TunnelRetry;
            while (!stoppingToken.IsCancellationRequested)
            {
                var identity = _identity;
                if (identity == null && TunnelState == TunnelStates.Retrying)
                {
                    MoveTunnel(TunnelStates.Unpaired);
                }

                if (identity != null)
                {
                    MoveTunnel(TunnelStates.Connecting);
                    try
                    {
                        var uri = new UriBuilder(new Uri(_http.BaseAddress, "api/gateways/tunnel")) { Scheme = _http.BaseAddress.Scheme == Uri.UriSchemeHttps ? "wss" : "ws" }.Uri;
                        var socket = await _transport.Connect(uri, identity.Token, stoppingToken).ConfigureAwait(false);
                        await using (var connection = new Tunnel.TunnelConnection(socket, _tunnel.ExecuteAsync, _logger))
                        {
                            _connection = connection;
                            MoveTunnel(TunnelStates.Open);
                            _logger?.LogInformation("Tunnel to the control plane open");
                            delay = TunnelRetry;
                            await connection.RunAsync(stoppingToken).ConfigureAwait(false);
                        }

                        _connection = null;
                        MoveTunnel(TunnelStates.Retrying);
                        _logger?.LogWarning("Tunnel to the control plane closed: reconnecting");
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex) when (ex is System.Net.WebSockets.WebSocketException || ex is HttpRequestException || ex is IOException || ex is InvalidOperationException)
                    {
                        _connection = null;
                        MoveTunnel(TunnelStates.Retrying);
                        _logger?.LogWarning("Cannot open the tunnel to the control plane: {Message}", ex.Message);
                    }
                }

                try
                {
                    await Task.Delay(identity == null ? PollInterval : delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, TunnelMaxRetry.Ticks));
            }
        }

        private async Task ReportLoopAsync(CancellationToken stoppingToken)
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
                request.Content = JsonContent.Create(new { version = GatewayService.Version, startedAt = _startedAt, health = _measure(), cameras = _cameras() });
                var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Removed at the control plane: forget the token and pair again. A gateway started with its identity
                    // (hosted in the cloud) never pairs: whoever started it stops it, or starts it again with a new one.
                    _credentials.Clear();
                    _identity = _credentials.Load();
                    if (_identity != null)
                    {
                        _logger?.LogWarning("The control plane does not accept this gateway's configured token");
                        return RetryInterval;
                    }

                    _logger?.LogWarning("The control plane no longer knows this gateway: pairing again");
                    return TimeSpan.Zero;
                }

                response.EnsureSuccessStatusCode();
                return HeartbeatInterval;
            }
        }

        #endregion
    }
}
