// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Events;

namespace Onvif.Open.Recording.Implementation.Recorder
{
    /// <summary>
    /// Reads a camera's events while its recorder runs, forwards them, and drives the recording trigger for
    /// the configured topics.
    /// </summary>
    internal sealed class CameraEventMonitor
    {
        #region Constructor

        public CameraEventMonitor(
            string cameraId,
            Func<CancellationToken, IAsyncEnumerable<CameraEvent>> eventSource,
            IReadOnlyList<string> triggerTopics,
            RecordingTrigger trigger,
            TimeProvider timeProvider,
            ILogger logger,
            Action<CameraEvent> onEvent)
        {
            _cameraId = cameraId;
            _eventSource = eventSource;
            _triggerTopics = triggerTopics ?? Array.Empty<string>();
            _trigger = trigger;
            _timeProvider = timeProvider;
            _logger = logger;
            _onEvent = onEvent;
        }

        #endregion

        #region Private Variables

        private readonly string _cameraId;
        private readonly Func<CancellationToken, IAsyncEnumerable<CameraEvent>> _eventSource;
        private readonly IReadOnlyList<string> _triggerTopics;
        private readonly RecordingTrigger _trigger;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly Action<CameraEvent> _onEvent;

        #endregion

        #region Public Methods

        public Task RunAsync(CancellationToken cancellationToken)
        {
            return SourceMonitor.RunAsync(_eventSource, Handle, _cameraId, "event subscription", _timeProvider, _logger, cancellationToken);
        }

        /// <summary>Applies one event to the trigger and forwards it (internal for tests).</summary>
        internal void Handle(CameraEvent cameraEvent)
        {
            cameraEvent = cameraEvent.WithCameraId(_cameraId);

            if (_trigger != null && _triggerTopics.Any(cameraEvent.MatchesTopic))
            {
                var now = _timeProvider.GetUtcNow();
                var source = cameraEvent.TopicPath + "|" + string.Join(",", cameraEvent.Source.OrderBy(s => s.Key).Select(s => s.Key + "=" + s.Value));

                switch (cameraEvent.GetState())
                {
                    case true:
                        _trigger.Activate(source, now);
                        break;
                    case false:
                        _trigger.Deactivate(source, now);
                        break;
                    default:
                        _trigger.Pulse(TimeSpan.Zero, now);
                        break;
                }
            }

            try
            {
                _onEvent?.Invoke(cameraEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Camera {CameraId}: event handler failed", _cameraId);
            }
        }

        #endregion
    }
}
