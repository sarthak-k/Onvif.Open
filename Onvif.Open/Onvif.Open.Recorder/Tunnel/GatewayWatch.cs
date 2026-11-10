// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Tunnel
{
    /// <summary>
    /// A whole site going dark (power, internet) says nothing by itself: its gateway just stops reporting. Every
    /// <see cref="Interval"/> the control plane checks the gateways that record its cameras; when one has not reported
    /// for <see cref="GatewayService.OnlineWindow"/> and has no tunnel open, its cameras get an offline event ("Gateway
    /// offline", from when it was last heard of), so offline alert rules catch it; the events end when it is back.
    /// </summary>
    public sealed class GatewayWatch : BackgroundService
    {
        public GatewayWatch(CameraDirectory directory, IGatewayStore gateways, GatewayService service, TunnelRegistry tunnels, EventRecorder events, ILogger<GatewayWatch> logger, TimeProvider timeProvider = null)
        {
            _directory = directory;
            _gateways = gateways;
            _service = service;
            _tunnels = tunnels;
            _events = events;
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

        private readonly CameraDirectory _directory;
        private readonly IGatewayStore _gateways;
        private readonly GatewayService _service;
        private readonly TunnelRegistry _tunnels;
        private readonly EventRecorder _events;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;

        /// <summary>Cameras marked offline because their gateway is.</summary>
        private readonly HashSet<string> _dark = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Checks every gateway once (also used by tests); the cameras now marked offline because of their gateway.</summary>
        public async Task<IReadOnlyCollection<string>> CheckAsync(CancellationToken cancellationToken = default)
        {
            var local = _service.Local?.Id;
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var cameras = _directory.All.Where(c => c.GatewayId != null && !string.Equals(c.GatewayId, local, StringComparison.OrdinalIgnoreCase)).ToList();
            var gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in cameras.GroupBy(c => c.GatewayId, StringComparer.OrdinalIgnoreCase))
            {
                var gateway = await _gateways.FindAsync(group.Key, cancellationToken).ConfigureAwait(false);
                var online = gateway != null && (_tunnels.IsConnected(gateway.Id) || _service.IsOnline(gateway));
                foreach (var camera in group)
                {
                    if (!online)
                    {
                        gone.Add(camera.Id);
                        if (_dark.Add(camera.Id))
                        {
                            _events.BeginGatewayOffline(camera.Id, gateway?.LastSeenAt is DateTime seen ? DateTime.SpecifyKind(seen, DateTimeKind.Utc) : now);
                        }
                    }
                    else if (_dark.Remove(camera.Id))
                    {
                        _events.EndGatewayOffline(camera.Id, now);
                    }
                }

                if (!online)
                {
                    _logger?.LogDebug("Gateway {GatewayId} is not reporting: {Count} camera(s) offline", group.Key, group.Count());
                }
            }

            // Cameras removed, or moved to this recorder, since: no longer offline because of a gateway.
            foreach (var stale in _dark.Where(id => !cameras.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))).ToList())
            {
                _dark.Remove(stale);
                _events.EndGatewayOffline(stale, now);
            }

            return gone;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (var timer = new PeriodicTimer(Interval, _timeProvider))
            {
                try
                {
                    while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                    {
                        try
                        {
                            await CheckAsync(stoppingToken).ConfigureAwait(false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            _logger?.LogWarning(ex, "Could not check the gateways");
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }
            }
        }
    }

    /// <summary>
    /// Where an export's video comes from: this recorder's recordings, or, for a camera another gateway records, a clip
    /// that gateway sends through its tunnel (then watermarked here like any other).
    /// </summary>
    public sealed class ClipSource
    {
        public ClipSource(Onvif.Open.Core.Abstract.Interface.Recording.IRecordingManager recording, CameraDirectory directory, GatewayService gateways, TunnelRegistry tunnels)
        {
            _recording = recording;
            _directory = directory;
            _gateways = gateways;
            _tunnels = tunnels;
        }

        private readonly Onvif.Open.Core.Abstract.Interface.Recording.IRecordingManager _recording;
        private readonly CameraDirectory _directory;
        private readonly GatewayService _gateways;
        private readonly TunnelRegistry _tunnels;

        /// <summary>Whether this recorder records the camera itself (else another gateway does).</summary>
        public bool IsLocal(string cameraId)
        {
            var camera = _directory.Find(cameraId);
            var local = _gateways.Local?.Id;
            return camera?.GatewayId == null || local == null || string.Equals(camera.GatewayId, local, StringComparison.OrdinalIgnoreCase)
                || _recording.GetStatus().Any(s => string.Equals(s.CameraId, cameraId, StringComparison.OrdinalIgnoreCase));
        }

        public async Task WriteAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, Stream destination, CancellationToken cancellationToken)
        {
            var camera = _directory.Find(cameraId);
            if (IsLocal(cameraId))
            {
                await Onvif.Open.Recording.Implementation.Export.ClipExport.WriteAsync(_recording, cameraId, from, to, destination, cancellationToken).ConfigureAwait(false);
                return;
            }

            var tunnel = _tunnels.Find(camera.GatewayId) ?? throw new InvalidOperationException("The gateway that records this camera is offline: try again when it is back.");
            var query = $"?from={Uri.EscapeDataString(from.UtcDateTime.ToString("o"))}&to={Uri.EscapeDataString(to.UtcDateTime.ToString("o"))}";
            var head = new TunnelRequestHead("GET", $"/api/cameras/{Uri.EscapeDataString(cameraId)}/clip", query, new Dictionary<string, string>(), camera.OrganizationId, "export");
            await using (var response = await tunnel.SendAsync(head, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false))
            {
                if (response.Status == 404)
                {
                    throw new InvalidOperationException("Nothing was recorded in that time.");
                }

                if (response.Status != 200)
                {
                    throw new InvalidOperationException($"The gateway could not send the video ({response.Status}).");
                }

                await response.Body.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
