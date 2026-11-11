// Sarthak Killedar
// 2026-11-11

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>Where starting a gateway hosted in the cloud stands (stored with it, see <see cref="Gateway.HostState"/>).</summary>
    public static class CloudGatewayStates
    {
        public const string Starting = "Starting";
        public const string Running = "Running";
        public const string Failed = "Failed";

        public static Onvif.Open.Core.StateMachines.StateMachine<string> Machine { get; } = new Onvif.Open.Core.StateMachines.StateMachine<string>(
            "Cloud gateway",
            Starting,
            (Starting, Running, "it connected"),
            (Starting, Failed, "it could not be started, or did not connect in time"),
            (Failed, Running, "it connected after all"),
            (Failed, Starting, "an administrator started it again"),
            (Running, Starting, "an administrator started it again"));
    }

    /// <summary>
    /// Gateways this control plane runs for customers whose cameras are on the internet: one dedicated gateway per
    /// organization (by default), started with its own token, which it reports with like any paired gateway. Starting
    /// it again gives it a new token (the old one stops working). Removing it stops it.
    /// </summary>
    public sealed class CloudGateways : BackgroundService
    {
        #region Constructor

        public CloudGateways(GatewayService gateways, IGatewayStore store, ICloudGatewayHost host, CloudOptions cloud, ILogger<CloudGateways> logger, TimeProvider timeProvider = null)
        {
            _gateways = gateways ?? throw new ArgumentNullException(nameof(gateways));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _cloud = cloud ?? new CloudOptions();
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Private Variables

        private readonly GatewayService _gateways;
        private readonly IGatewayStore _store;
        private readonly ICloudGatewayHost _host;
        private readonly CloudOptions _cloud;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);

        #endregion

        #region Public Methods

        public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

        /// <summary>Whether organizations can have a gateway hosted here.</summary>
        public bool IsAvailable => _cloud.Enabled && _host.IsConfigured && _cloud.Gateways.PerOrganization > 0;

        /// <summary>A new hosted gateway for the organization, being started; failing to start is a state, not an error.</summary>
        public async Task<ServiceResult<Gateway>> CreateAsync(string organizationId, string name, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable)
            {
                return ServiceResult<Gateway>.NotFound();
            }

            if (name != null && (string.IsNullOrWhiteSpace(name) || name.Trim().Length > GatewayService.MaxNameLength))
            {
                return ServiceResult<Gateway>.Invalid(new[] { $"Name is required and at most {GatewayService.MaxNameLength} characters." });
            }

            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var hosted = (await _gateways.GetAsync(organizationId, cancellationToken).ConfigureAwait(false)).Count(g => g.IsHosted);
                var limit = _cloud.Gateways.PerOrganization;
                if (hosted >= limit)
                {
                    return ServiceResult<Gateway>.Conflict(limit == 1 ? "This organization has its cloud gateway already." : $"This organization has its {limit} cloud gateways already.");
                }

                var (gateway, token) = await _gateways.CreatePairedAsync(organizationId, name ?? "Cloud gateway", GatewayLocations.Cloud, cancellationToken).ConfigureAwait(false);
                await SetAsync(gateway, CloudGatewayStates.Machine.Initial, null, GatewayTokens.Hash(token), cancellationToken).ConfigureAwait(false);
                await LaunchAsync(gateway, token, cancellationToken).ConfigureAwait(false);
                return ServiceResult<Gateway>.Ok(gateway);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Stops a hosted gateway and starts it again, with a new token.</summary>
        public async Task<ServiceResult<Gateway>> StartAgainAsync(string organizationId, string id, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var gateway = await _gateways.FindAsync(organizationId, id, cancellationToken).ConfigureAwait(false);
                if (gateway == null || !gateway.IsHosted || !IsAvailable)
                {
                    return ServiceResult<Gateway>.NotFound();
                }

                if (!CloudGatewayStates.Machine.CanMove(gateway.HostState, CloudGatewayStates.Starting))
                {
                    return ServiceResult<Gateway>.Conflict("It is starting: wait until it connects, or fails.");
                }

                await StopQuietlyAsync(gateway.Id, cancellationToken).ConfigureAwait(false);
                var token = GatewayTokens.Create();
                await SetAsync(gateway, CloudGatewayStates.Starting, null, GatewayTokens.Hash(token), cancellationToken).ConfigureAwait(false);
                await LaunchAsync(gateway, token, cancellationToken).ConfigureAwait(false);
                return ServiceResult<Gateway>.Ok(gateway);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>A gateway reported: a hosted one that was starting (or had given up) is running.</summary>
        public async Task ReportedAsync(Gateway gateway, CancellationToken cancellationToken = default)
        {
            if (gateway?.IsHosted == true && CloudGatewayStates.Machine.CanMove(gateway.HostState, CloudGatewayStates.Running))
            {
                await SetAsync(gateway, CloudGatewayStates.Running, null, null, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>A gateway was removed: a hosted one is stopped (its token no longer works either way).</summary>
        public async Task RemovedAsync(Gateway gateway, CancellationToken cancellationToken = default)
        {
            if (gateway?.IsHosted == true)
            {
                await StopQuietlyAsync(gateway.Id, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Gateways still starting after <see cref="CloudGatewayOptions.StartTimeout"/> have failed. Returns how many.</summary>
        public async Task<int> CheckAsync(CancellationToken cancellationToken = default)
        {
            var now = Now;
            var failed = 0;
            foreach (var gateway in await _store.GetHostedAsync(CloudGatewayStates.Starting, cancellationToken).ConfigureAwait(false))
            {
                if (gateway.HostChangedAt is DateTime since && now - since >= _cloud.Gateways.StartTimeout)
                {
                    await SetAsync(gateway, CloudGatewayStates.Failed, $"It did not connect within {_cloud.Gateways.StartTimeout.TotalMinutes:0} minutes: check that it can reach {ControlPlaneUrl}.", null, cancellationToken).ConfigureAwait(false);
                    failed++;
                }
            }

            return failed;
        }

        #endregion

        #region Protected Methods

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!IsAvailable)
            {
                return;
            }

            using (var timer = new PeriodicTimer(CheckInterval, _timeProvider))
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    try
                    {
                        await CheckAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger?.LogWarning(ex, "Checking the cloud gateways failed");
                    }
                }
            }
        }

        #endregion

        #region Private Methods

        private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

        private string ControlPlaneUrl => (_cloud.Gateways.ControlPlaneUrl ?? _cloud.WebUrl)?.TrimEnd('/');

        private async Task LaunchAsync(Gateway gateway, string token, CancellationToken cancellationToken)
        {
            try
            {
                await _host.StartAsync(new CloudGatewayLaunch(gateway.Id, token, ControlPlaneUrl), cancellationToken).ConfigureAwait(false);
                _logger?.LogInformation("Cloud gateway {Name} ({Id}) of organization {Organization} started", gateway.Name, gateway.Id, gateway.OrganizationId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning(ex, "Cloud gateway {Id} could not be started", gateway.Id);
                await SetAsync(gateway, CloudGatewayStates.Failed, ex.Message, null, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task StopQuietlyAsync(string id, CancellationToken cancellationToken)
        {
            try
            {
                await _host.StopAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Its token is revoked or replaced already, so a gateway left running can do nothing: it is only cost.
                _logger?.LogWarning(ex, "Cloud gateway {Id} could not be stopped: stop it by hand", id);
            }
        }

        /// <summary>Moves the gateway through <see cref="CloudGatewayStates.Machine"/> (the first state is set without a move).</summary>
        private async Task SetAsync(Gateway gateway, string state, string error, string tokenHash, CancellationToken cancellationToken)
        {
            var to = gateway.HostState == null ? state : CloudGatewayStates.Machine.Move(gateway.HostState, state);
            var now = Now;
            var message = error == null ? null : error.Length <= CommandCloudGatewayHost.MaxError ? error : error.Substring(0, CommandCloudGatewayHost.MaxError - 1) + "…";
            await _store.UpdateHostAsync(gateway.Id, to, message, now, tokenHash, cancellationToken).ConfigureAwait(false);
            gateway.HostState = to;
            gateway.HostError = message;
            gateway.HostChangedAt = now;
            gateway.TokenHash = tokenHash ?? gateway.TokenHash;
        }

        #endregion
    }
}
