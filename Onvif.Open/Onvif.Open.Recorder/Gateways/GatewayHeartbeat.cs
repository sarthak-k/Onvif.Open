// Sarthak Killedar
// 2026-11-09

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>
    /// Reports the embedded gateway's health (cameras, disk, backup) and that it is alive, every
    /// <see cref="Interval"/>; a gateway that stops reporting shows as offline.
    /// </summary>
    public sealed class GatewayHeartbeat : BackgroundService
    {
        #region Constructor

        public GatewayHeartbeat(GatewayService gateways, IRecordingManager recording, TimeProvider timeProvider, ILogger<GatewayHeartbeat> logger)
        {
            _gateways = gateways ?? throw new ArgumentNullException(nameof(gateways));
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _timeProvider = timeProvider ?? TimeProvider.System;
            _logger = logger;
        }

        #endregion

        #region Public Variables

        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

        #endregion

        #region Private Variables

        private readonly GatewayService _gateways;
        private readonly IRecordingManager _recording;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;

        #endregion

        #region Public Methods

        /// <summary>What the gateway reports now.</summary>
        public static GatewayHealth Measure(IRecordingManager recording)
        {
            var cameras = recording.GetStatus();
            var storage = recording.GetStorageInformation();

            return new GatewayHealth
            {
                Cameras = cameras.Count,
                Recording = cameras.Count(c => c.State == RecordingState.Recording),
                Failing = cameras.Count(c => c.State == RecordingState.Reconnecting),
                FreeBytes = storage.AvailableFreeBytes,
                RecordedBytes = recording.GetSegments().Sum(s => s.Size),
                BackupProvider = storage.Archive?.Provider,
                PendingUploads = storage.Archive?.PendingUploads,
                BackupError = storage.Archive?.LastError
            };
        }

        /// <summary>Reports right away (after setup created the gateway, for instance).</summary>
        public Task ReportNowAsync(CancellationToken cancellationToken = default) =>
            _gateways.ReportAsync(Measure(_recording), cancellationToken);

        #endregion

        #region Protected Methods

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using (var timer = new PeriodicTimer(Interval, _timeProvider))
            {
                do
                {
                    try
                    {
                        await ReportNowAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Could not report the gateway's health");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
            }
        }

        #endregion
    }
}
