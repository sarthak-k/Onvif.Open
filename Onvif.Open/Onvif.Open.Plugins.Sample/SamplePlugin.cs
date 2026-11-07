// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Plugins.Sample
{
    /// <summary>
    /// Template plugin showing every extension point. It only counts frames and logs notifications;
    /// replace the bodies with your own logic (analytics, schedules, notifications, uploads, ...).
    /// </summary>
    /// <remarks>
    /// Settings (Plugins:Items:Onvif.Open.Plugins.Sample:Settings in appsettings.json):
    /// <c>ReportEveryFrames</c> — publish a "sample.frames" event every N key frames per camera (default 100).
    /// </remarks>
    public sealed class SamplePlugin : IFrameProcessor, ICameraStateObserver, ISegmentObserver, IPluginEventObserver
    {
        #region Private Variables

        private readonly ConcurrentDictionary<string, long> _keyFrames = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private IPluginContext _context;
        private int _reportEvery = 100;

        #endregion

        #region Public Variables

        public string Name => "Sample";

        /// <summary>Analytics usually only need one picture per GOP.</summary>
        public FrameFilter FrameFilter => FrameFilter.KeyFramesOnly;

        #endregion

        #region Public Methods

        public Task StartAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            _context = context;

            if (context.Settings.TryGetValue("ReportEveryFrames", out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
            {
                _reportEvery = parsed;
            }

            context.Logger.LogInformation("Sample plugin started for {Count} camera(s)", context.Cameras.GetStatus().Count);
            return Task.CompletedTask;
        }

        public async ValueTask ProcessFrameAsync(CameraFrame frame, CancellationToken cancellationToken)
        {
            // frame.Frame.Data is an H.264/H.265 access unit; decode it with the library of your choice,
            // or call _context.Cameras.GetSnapshotAsync(frame.CameraId) for a JPEG.
            var count = _keyFrames.AddOrUpdate(frame.CameraId, 1, (_, c) => c + 1);

            if (count % _reportEvery == 0)
            {
                await _context.PublishAsync(new PluginEvent(
                    "sample.frames",
                    frame.CameraId,
                    $"{count} key frames received",
                    PluginEventSeverity.Information,
                    new Dictionary<string, string> { ["keyFrames"] = count.ToString(CultureInfo.InvariantCulture) },
                    frame.ReceivedAt), cancellationToken).ConfigureAwait(false);
            }
        }

        public Task OnCameraStateChangedAsync(CameraStateChange change, CancellationToken cancellationToken)
        {
            _context.Logger.LogInformation("Camera {CameraId}: {Previous} -> {State} {Error}", change.CameraId, change.PreviousState, change.State, change.Error);
            return Task.CompletedTask;
        }

        public Task OnSegmentCompletedAsync(RecordingSegment segment, CancellationToken cancellationToken)
        {
            _context.Logger.LogInformation("Camera {CameraId}: segment {File} completed ({Size} bytes)", segment.CameraId, segment.FileName, segment.Size);
            return Task.CompletedTask;
        }

        public Task OnPluginEventAsync(PluginEvent pluginEvent, CancellationToken cancellationToken)
        {
            _context.Logger.LogInformation("Event {Type} from {Source}: {Message}", pluginEvent.Type, pluginEvent.Source, pluginEvent.Message);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        #endregion
    }
}
