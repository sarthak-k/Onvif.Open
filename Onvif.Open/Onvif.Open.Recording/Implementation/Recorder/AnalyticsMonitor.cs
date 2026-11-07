// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Analytics;

namespace Onvif.Open.Recording.Implementation.Recorder
{
    /// <summary>
    /// Reads a camera's analytics metadata while its recorder runs, forwards it, and triggers recording while
    /// an object of a configured class is in view.
    /// </summary>
    internal sealed class AnalyticsMonitor
    {
        #region Constructor

        public AnalyticsMonitor(
            string cameraId,
            Func<CancellationToken, IAsyncEnumerable<AnalyticsFrame>> analyticsSource,
            IReadOnlyList<string> triggerClasses,
            double minLikelihood,
            RecordingTrigger trigger,
            TimeProvider timeProvider,
            ILogger logger,
            Action<AnalyticsFrame> onAnalytics)
        {
            _cameraId = cameraId;
            _analyticsSource = analyticsSource;
            _triggerClasses = triggerClasses ?? Array.Empty<string>();
            _minLikelihood = minLikelihood;
            _trigger = trigger;
            _timeProvider = timeProvider;
            _logger = logger;
            _onAnalytics = onAnalytics;
        }

        #endregion

        #region Private Variables

        private readonly string _cameraId;
        private readonly Func<CancellationToken, IAsyncEnumerable<AnalyticsFrame>> _analyticsSource;
        private readonly IReadOnlyList<string> _triggerClasses;
        private readonly double _minLikelihood;
        private readonly RecordingTrigger _trigger;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly Action<AnalyticsFrame> _onAnalytics;

        #endregion

        #region Public Methods

        public Task RunAsync(CancellationToken cancellationToken)
        {
            return SourceMonitor.RunAsync(_analyticsSource, Handle, _cameraId, "analytics stream", _timeProvider, _logger, cancellationToken);
        }

        /// <summary>Applies one analytics frame to the trigger and forwards it (internal for tests).</summary>
        internal void Handle(AnalyticsFrame frame)
        {
            frame = frame.WithCameraId(_cameraId);

            // Cameras report objects several times per second while they are in view: each frame
            // extends the recording, and the post-roll covers the time after the object has left.
            if (_trigger != null && _triggerClasses.Any(c => frame.Contains(c, _minLikelihood)))
            {
                _trigger.Pulse(TimeSpan.Zero, _timeProvider.GetUtcNow());
            }

            try
            {
                _onAnalytics?.Invoke(frame);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Camera {CameraId}: analytics handler failed", _cameraId);
            }
        }

        #endregion
    }
}
