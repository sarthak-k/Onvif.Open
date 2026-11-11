// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>
    /// "Cloud:Gateways": how this control plane runs a dedicated gateway for a customer whose cameras are on the internet
    /// (a container, a pod, a VM...). Commands, not a cloud SDK, so any platform works.
    /// </summary>
    public sealed class CloudGatewayOptions
    {
        /// <summary>
        /// Starts the gateway; "{gatewayId}" in the arguments is replaced (a UUID). Its identity is passed in the
        /// environment, never in the arguments: ControlPlane__Url, ControlPlane__GatewayId, ControlPlane__Token and
        /// Gateway__Location=Cloud (e.g. docker run -e ControlPlane__Token ...). Null: no hosted gateways.
        /// </summary>
        public HostCommand Start { get; set; }

        /// <summary>Stops and removes it (when it is removed, or before it is started again).</summary>
        public HostCommand Stop { get; set; }

        /// <summary>Where the gateways reach this control plane; default: this region's web app address.</summary>
        public string ControlPlaneUrl { get; set; }

        /// <summary>How many each organization may have.</summary>
        public int PerOrganization { get; set; } = 1;

        /// <summary>How long a command may run.</summary>
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <summary>A started gateway that has not reported by then has failed.</summary>
        public TimeSpan StartTimeout { get; set; } = TimeSpan.FromMinutes(10);

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Start?.Command);

        public IEnumerable<string> Check()
        {
            if (IsConfigured && string.IsNullOrWhiteSpace(Stop?.Command))
            {
                yield return "Cloud:Gateways:Stop is needed with Cloud:Gateways:Start, so removed gateways stop.";
            }

            if (ControlPlaneUrl != null && !Uri.TryCreate(ControlPlaneUrl, UriKind.Absolute, out _))
            {
                yield return "Cloud:Gateways:ControlPlaneUrl must be an absolute URL.";
            }

            if (PerOrganization < 0)
            {
                yield return "Cloud:Gateways:PerOrganization cannot be negative.";
            }
        }
    }

    public sealed class HostCommand
    {
        public string Command { get; set; }
        public List<string> Arguments { get; set; } = new List<string>();
    }

    /// <summary>What a hosted gateway is started with.</summary>
    public sealed record CloudGatewayLaunch(string GatewayId, string Token, string ControlPlaneUrl);

    /// <summary>Runs and stops the gateways this control plane hosts.</summary>
    public interface ICloudGatewayHost
    {
        bool IsConfigured { get; }

        /// <summary>Starts it; throws with what went wrong.</summary>
        Task StartAsync(CloudGatewayLaunch launch, CancellationToken cancellationToken = default);

        Task StopAsync(string gatewayId, CancellationToken cancellationToken = default);
    }

    /// <summary><see cref="ICloudGatewayHost"/> with the commands of <see cref="CloudGatewayOptions"/>; no shell is involved.</summary>
    public sealed class CommandCloudGatewayHost : ICloudGatewayHost
    {
        public CommandCloudGatewayHost(CloudGatewayOptions options, ILogger<CommandCloudGatewayHost> logger)
        {
            _options = options ?? new CloudGatewayOptions();
            _logger = logger;
        }

        public const int MaxError = 500;

        private readonly CloudGatewayOptions _options;
        private readonly ILogger _logger;

        public bool IsConfigured => _options.IsConfigured;

        public Task StartAsync(CloudGatewayLaunch launch, CancellationToken cancellationToken = default) =>
            RunAsync(_options.Start, launch.GatewayId, new Dictionary<string, string>
            {
                ["ControlPlane__Url"] = launch.ControlPlaneUrl,
                ["ControlPlane__GatewayId"] = launch.GatewayId,
                ["ControlPlane__Token"] = launch.Token,
                ["Gateway__Location"] = GatewayLocations.Cloud
            }, cancellationToken);

        public Task StopAsync(string gatewayId, CancellationToken cancellationToken = default) =>
            RunAsync(_options.Stop, gatewayId, new Dictionary<string, string>(), cancellationToken);

        private async Task RunAsync(HostCommand command, string gatewayId, IReadOnlyDictionary<string, string> environment, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(command?.Command))
            {
                throw new InvalidOperationException("Gateways are not hosted here.");
            }

            if (!Guid.TryParse(gatewayId, out _))
            {
                throw new ArgumentException("A gateway id is a UUID.", nameof(gatewayId));
            }

            var start = new ProcessStartInfo(command.Command)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in command.Arguments ?? new List<string>())
            {
                start.ArgumentList.Add(argument.Replace("{gatewayId}", gatewayId, StringComparison.Ordinal));
            }

            foreach (var (name, value) in environment)
            {
                start.Environment[name] = value;
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var process = Process.Start(start) ?? throw new InvalidOperationException($"{command.Command} did not start."))
            {
                timeout.CancelAfter(_options.CommandTimeout);
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                try
                {
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    await Task.WhenAll(output, error).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException($"{command.Command} did not finish within {_options.CommandTimeout.TotalSeconds:0} s.");
                }

                if (process.ExitCode != 0)
                {
                    var message = (error.Result.Trim().Length > 0 ? error.Result : output.Result).Trim();
                    _logger?.LogWarning("{Command} for gateway {Id} exited with {Code}: {Message}", command.Command, gatewayId, process.ExitCode, message);
                    throw new InvalidOperationException(Truncate($"{command.Command} exited with {process.ExitCode}: {message}"));
                }
            }
        }

        private static string Truncate(string text) => text.Length <= MaxError ? text : text.Substring(0, MaxError - 1) + "…";
    }
}
