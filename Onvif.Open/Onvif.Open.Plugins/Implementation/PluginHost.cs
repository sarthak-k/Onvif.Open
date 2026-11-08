// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Core.Abstract.Interface.Replay;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Plugins.Implementation.Runtime;

namespace Onvif.Open.Plugins.Implementation
{
    /// <summary>
    /// Runs plugins next to a recording manager. Build it with <see cref="Builder.PluginHostBuilder"/>.
    /// </summary>
    public sealed class PluginHost : IPluginHost
    {
        #region Constructor

        private PluginHost(
            IRecordingManager manager,
            IReadOnlyList<PluginRegistration> registrations,
            Func<string, CancellationToken, Task<byte[]>> snapshotProvider,
            int frameQueueCapacity,
            int notificationQueueCapacity,
            int eventHistoryCapacity,
            TimeSpan stopTimeout,
            TimeProvider timeProvider,
            ILoggerFactory loggerFactory,
            Func<string, ICameraPtz> ptzProvider,
            Func<string, ICameraImaging> imagingProvider,
            Func<string, ICameraIo> ioProvider,
            Func<string, ICameraRecordings> deviceRecordingsProvider)
        {
            _manager = manager;
            _timeProvider = timeProvider;
            _stopTimeout = stopTimeout;
            _events = new PluginEventStore(eventHistoryCapacity);
            _logger = loggerFactory.CreateLogger<PluginHost>();

            _runners = registrations.Select(r =>
            {
                var cameras = new PluginCameras(manager, r.Cameras, snapshotProvider, ptzProvider, imagingProvider, ioProvider, deviceRecordingsProvider);
                var context = new PluginContext(r.Plugin.Name, r.Settings, loggerFactory.CreateLogger($"Plugin.{r.Plugin.Name}"), timeProvider, cameras, PublishAsync);
                return new PluginRunner(r, context, frameQueueCapacity, notificationQueueCapacity, timeProvider, loggerFactory.CreateLogger<PluginRunner>());
            }).ToList();
        }

        #endregion

        #region Private Variables

        private readonly IRecordingManager _manager;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _stopTimeout;
        private readonly PluginEventStore _events;
        private readonly ILogger _logger;
        private readonly IReadOnlyList<PluginRunner> _runners;
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();
        private bool _started;

        #endregion

        #region Public Variables

        public event EventHandler<PluginEvent> EventPublished;

        #endregion

        #region Public Methods

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_started)
                {
                    return;
                }

                _started = true;
            }

            await Task.WhenAll(_runners.Select(r => r.StartAsync(cancellationToken))).ConfigureAwait(false);

            _manager.FrameReceived += OnFrameReceived;
            _manager.CameraStateChanged += OnCameraStateChanged;
            _manager.SegmentCompleted += OnSegmentCompleted;
            _manager.CameraEventReceived += OnCameraEventReceived;
            _manager.AnalyticsReceived += OnAnalyticsReceived;
            _manager.AudioReceived += OnAudioReceived;

            _logger.LogInformation("{Running} of {Count} plugin(s) running", _runners.Count(r => r.State == PluginState.Running), _runners.Count);
        }

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (!_started)
                {
                    return;
                }

                _started = false;
            }

            _manager.FrameReceived -= OnFrameReceived;
            _manager.CameraStateChanged -= OnCameraStateChanged;
            _manager.SegmentCompleted -= OnSegmentCompleted;
            _manager.CameraEventReceived -= OnCameraEventReceived;
            _manager.AnalyticsReceived -= OnAnalyticsReceived;
            _manager.AudioReceived -= OnAudioReceived;

            await Task.WhenAll(_runners.Select(r => r.StopAsync(_stopTimeout, cancellationToken))).ConfigureAwait(false);
        }

        public IReadOnlyList<PluginStatus> GetStatus()
        {
            return _runners.Select(r => r.GetStatus()).ToList();
        }

        public IReadOnlyList<PluginEvent> GetEvents(PluginEventQuery query = null)
        {
            return _events.Query(query);
        }

        public ValueTask DisposeAsync()
        {
            return new ValueTask(StopAsync());
        }

        internal static PluginHost CreateInstance(
            IRecordingManager manager,
            IReadOnlyList<PluginRegistration> registrations,
            Func<string, CancellationToken, Task<byte[]>> snapshotProvider,
            int frameQueueCapacity,
            int notificationQueueCapacity,
            int eventHistoryCapacity,
            TimeSpan stopTimeout,
            TimeProvider timeProvider,
            ILoggerFactory loggerFactory,
            Func<string, ICameraPtz> ptzProvider = null,
            Func<string, ICameraImaging> imagingProvider = null,
            Func<string, ICameraIo> ioProvider = null,
            Func<string, ICameraRecordings> deviceRecordingsProvider = null)
        {
            return new PluginHost(manager, registrations, snapshotProvider, frameQueueCapacity, notificationQueueCapacity, eventHistoryCapacity, stopTimeout, timeProvider, loggerFactory, ptzProvider, imagingProvider, ioProvider, deviceRecordingsProvider);
        }

        #endregion

        #region Private Methods

        private Task PublishAsync(string source, PluginEvent pluginEvent, CancellationToken cancellationToken)
        {
            pluginEvent.Source = source;

            if (pluginEvent.Timestamp == default)
            {
                pluginEvent.Timestamp = _timeProvider.GetUtcNow();
            }

            _events.Add(pluginEvent);

            foreach (var runner in _runners)
            {
                if (string.Equals(runner.Name, source, StringComparison.OrdinalIgnoreCase))
                {
                    runner.CountPublishedEvent();
                }

                runner.OnPluginEvent(pluginEvent);
            }

            _logger.Log(ToLogLevel(pluginEvent.Severity), "Plugin event {Type} from {Source} for camera {CameraId}: {Message}", pluginEvent.Type, source, pluginEvent.CameraId, pluginEvent.Message);

            try
            {
                EventPublished?.Invoke(this, pluginEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EventPublished handler failed");
            }

            return Task.CompletedTask;
        }

        private void OnFrameReceived(object sender, CameraFrame frame)
        {
            foreach (var runner in _runners)
            {
                runner.OnFrame(frame);
            }
        }

        private void OnCameraStateChanged(object sender, CameraStateChange change)
        {
            foreach (var runner in _runners)
            {
                runner.OnStateChanged(change);
            }
        }

        private void OnCameraEventReceived(object sender, CameraEvent cameraEvent)
        {
            foreach (var runner in _runners)
            {
                runner.OnCameraEvent(cameraEvent);
            }
        }

        private void OnAudioReceived(object sender, CameraAudio audio)
        {
            foreach (var runner in _runners)
            {
                runner.OnAudio(audio);
            }
        }

        private void OnAnalyticsReceived(object sender, AnalyticsFrame frame)
        {
            foreach (var runner in _runners)
            {
                runner.OnAnalytics(frame);
            }
        }

        private void OnSegmentCompleted(object sender, RecordingSegment segment)
        {
            foreach (var runner in _runners)
            {
                runner.OnSegmentCompleted(segment);
            }
        }

        private static LogLevel ToLogLevel(PluginEventSeverity severity)
        {
            switch (severity)
            {
                case PluginEventSeverity.Critical:
                    return LogLevel.Error;
                case PluginEventSeverity.Warning:
                    return LogLevel.Warning;
                default:
                    return LogLevel.Information;
            }
        }

        #endregion
    }
}
