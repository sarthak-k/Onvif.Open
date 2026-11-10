// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Tunnel
{
    /// <summary>A camera a gateway records for the control plane, with the rules of its recording policy.</summary>
    public sealed class AssignedCamera
    {
        public CameraOptions Camera { get; set; }
        public RecordingPolicyRecord Policy { get; set; }
    }

    /// <summary>How a gateway's camera is doing, as its heartbeat reports it.</summary>
    public sealed record RemoteCameraStatus(
        string CameraId, string State, string Mode, bool IsTriggered, string Codec, bool RecordsAudio,
        long FramesRecorded, long BytesRecorded, int SegmentsCompleted, DateTimeOffset? LastFrameAt, string LastError, DateTimeOffset? LastErrorAt);

    /// <summary>
    /// The control plane's side of cameras recorded by other gateways: it sends each gateway the cameras it records
    /// (whenever they change, and when its tunnel opens), and keeps what the gateways report about them.
    /// </summary>
    public sealed class RemoteCameras
    {
        #region Constructor

        public RemoteCameras(TunnelRegistry tunnels, ISettingsStore store, ITenancyStore tenancy, ILogger<RemoteCameras> logger, TimeProvider timeProvider = null)
        {
            _tunnels = tunnels ?? throw new ArgumentNullException(nameof(tunnels));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _tenancy = tenancy ?? throw new ArgumentNullException(nameof(tenancy));
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _tunnels.Connected += gatewayId => _ = Task.Run(() => SyncAsync(gatewayId));
        }

        #endregion

        #region Public Variables

        /// <summary>Where the control plane sends a gateway its cameras (through the tunnel only).</summary>
        public const string SyncPath = "/api/gateway/cameras";

        /// <summary>A status older than this (three missed heartbeats) is out of date.</summary>
        public static readonly TimeSpan StatusLifetime = TimeSpan.FromSeconds(90);

        #endregion

        #region Private Variables

        private readonly TunnelRegistry _tunnels;
        private readonly ISettingsStore _store;
        private readonly ITenancyStore _tenancy;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly ConcurrentDictionary<string, (RemoteCameraStatus Status, string GatewayId, DateTimeOffset At)> _statuses = new ConcurrentDictionary<string, (RemoteCameraStatus, string, DateTimeOffset)>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _syncs = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Public Methods

        /// <summary>Sends a gateway the cameras it records; false when its tunnel is closed (it gets them when it reconnects).</summary>
        public async Task<bool> SyncAsync(string gatewayId, CancellationToken cancellationToken = default)
        {
            var gate = _syncs.GetOrAdd(gatewayId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var tunnel = _tunnels.Find(gatewayId);
                if (tunnel == null)
                {
                    return false;
                }

                var policies = (await _tenancy.GetPoliciesAsync(null, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Id, StringComparer.Ordinal);
                var cameras = (await _store.GetCamerasAsync(cancellationToken).ConfigureAwait(false))
                    .Where(c => string.Equals(c.GatewayId, gatewayId, StringComparison.OrdinalIgnoreCase) && c.PolicyId != null && policies.ContainsKey(c.PolicyId))
                    .Select(c => new AssignedCamera { Camera = RecordingPolicyRules.Apply(c, policies[c.PolicyId]), Policy = policies[c.PolicyId] })
                    .ToList();

                var head = new TunnelRequestHead("PUT", SyncPath, "", new Dictionary<string, string> { ["Content-Type"] = "application/json" }, null, "control plane");
                await using (var response = await tunnel.SendAsync(head, JsonSerializer.SerializeToUtf8Bytes(cameras, TunnelConnection.Json), cancellationToken).ConfigureAwait(false))
                {
                    if (response.Status >= 300)
                    {
                        _logger?.LogWarning("Gateway {GatewayId} refused its cameras ({Status})", gatewayId, response.Status);
                        return false;
                    }
                }

                _logger?.LogInformation("Gateway {GatewayId} records {Count} camera(s)", gatewayId, cameras.Count);
                return true;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.Net.WebSockets.WebSocketException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                _logger?.LogWarning("Could not send gateway {GatewayId} its cameras: {Error}", gatewayId, ex.Message);
                return false;
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>What a gateway's heartbeat says about its cameras.</summary>
        public void Report(string gatewayId, IEnumerable<RemoteCameraStatus> statuses)
        {
            var now = _timeProvider.GetUtcNow();
            foreach (var status in statuses ?? Enumerable.Empty<RemoteCameraStatus>())
            {
                if (!string.IsNullOrEmpty(status?.CameraId))
                {
                    _statuses[status.CameraId] = (status, gatewayId, now);
                }
            }
        }

        /// <summary>The last report about a camera from the gateway that records it; null when there is none, or it is out of date.</summary>
        public RemoteCameraStatus Find(string cameraId, string gatewayId)
        {
            return cameraId != null && _statuses.TryGetValue(cameraId, out var entry)
                && string.Equals(entry.GatewayId, gatewayId, StringComparison.OrdinalIgnoreCase)
                && _timeProvider.GetUtcNow() - entry.At < StatusLifetime
                ? entry.Status
                : null;
        }

        #endregion
    }
}

namespace Onvif.Open.Recorder.Tunnel
{
    /// <summary>The gateway's side: how the cameras the control plane assigned are doing, for the heartbeat.</summary>
    public static class RemoteCameraReport
    {
        public static IReadOnlyList<RemoteCameraStatus> Of(Onvif.Open.Core.Abstract.Interface.Recording.IRecordingManager manager, IReadOnlyCollection<string> cameraIds)
        {
            var assigned = new HashSet<string>(cameraIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return manager.GetStatus()
                .Where(s => assigned.Contains(s.CameraId))
                .Select(s => new RemoteCameraStatus(s.CameraId, s.State.ToString(), s.Mode.ToString(), s.IsTriggered, s.Codec.ToString(), s.RecordsAudio,
                    s.FramesRecorded, s.BytesRecorded, s.SegmentsCompleted, s.LastFrameAt, s.LastError, s.LastErrorAt))
                .ToList();
        }
    }
}
