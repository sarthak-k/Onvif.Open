// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Plugins.Implementation.Runtime
{
    /// <summary>
    /// Isolates one plugin: frames and notifications are queued per plugin and delivered by the plugin's
    /// own consumer loops, so a slow or failing plugin can neither block recording nor other plugins.
    /// </summary>
    internal sealed class PluginRunner
    {
        #region Constructor

        public PluginRunner(PluginRegistration registration, IPluginContext context, int frameQueueCapacity, int notificationQueueCapacity, TimeProvider timeProvider, ILogger logger)
        {
            Plugin = registration.Plugin;
            Registration = registration;
            _context = context;
            _timeProvider = timeProvider;
            _logger = logger;

            _frameProcessor = Plugin as IFrameProcessor;
            _stateObserver = Plugin as ICameraStateObserver;
            _segmentObserver = Plugin as ISegmentObserver;
            _eventObserver = Plugin as IPluginEventObserver;
            _cameraEventObserver = Plugin as ICameraEventObserver;
            _analyticsObserver = Plugin as IAnalyticsObserver;
            _audioProcessor = Plugin as IAudioProcessor;

            // Audio comes in many small frames (G.711: 50 per second): it gets its own, larger queue.
            _audio = Channel.CreateBounded<CameraAudio>(
                new BoundedChannelOptions(frameQueueCapacity * AudioQueueFactor) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
                _ => Interlocked.Increment(ref _audioDropped));

            _frames = Channel.CreateBounded<CameraFrame>(
                new BoundedChannelOptions(frameQueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
                _ => Interlocked.Increment(ref _framesDropped));

            _notifications = Channel.CreateBounded<Func<CancellationToken, Task>>(
                new BoundedChannelOptions(notificationQueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
                _ => Interlocked.Increment(ref _notificationsDropped));
        }

        #endregion

        #region Private Variables

        private readonly IPluginContext _context;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger _logger;
        private readonly IFrameProcessor _frameProcessor;
        private readonly ICameraStateObserver _stateObserver;
        private readonly ISegmentObserver _segmentObserver;
        private readonly IPluginEventObserver _eventObserver;
        private readonly ICameraEventObserver _cameraEventObserver;
        private readonly IAnalyticsObserver _analyticsObserver;
        private readonly IAudioProcessor _audioProcessor;
        private readonly Channel<CameraAudio> _audio;
        private const int AudioQueueFactor = 8;
        private Task _audioLoop = Task.CompletedTask;
        private long _audioDelivered;
        private long _audioDropped;
        private readonly Channel<CameraFrame> _frames;
        private readonly Channel<Func<CancellationToken, Task>> _notifications;
        private readonly CancellationTokenSource _abort = new CancellationTokenSource();
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();

        private FrameFilter _frameFilter;
        private Task _frameLoop = Task.CompletedTask;
        private Task _notificationLoop = Task.CompletedTask;
        private volatile PluginState _state = PluginState.Created;
        private long _framesDelivered;
        private long _framesDropped;
        private long _notificationsDelivered;
        private long _notificationsDropped;
        private long _eventsPublished;
        private long _errors;
        private string _lastError;
        private DateTimeOffset? _lastErrorAt;

        #endregion

        #region Public Variables

        public IPlugin Plugin { get; }
        public PluginRegistration Registration { get; }
        public string Name => Plugin.Name;
        public PluginState State => _state;

        #endregion

        #region Public Methods

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Plugin.StartAsync(_context, cancellationToken).ConfigureAwait(false);
                _frameFilter = _frameProcessor?.FrameFilter ?? FrameFilter.AllFrames;
            }
            catch (Exception ex)
            {
                RecordError(ex, "start");
                _state = PluginState.Faulted;
                return;
            }

            _state = PluginState.Running;
            _frameLoop = Task.Run(() => FrameLoopAsync(_abort.Token));
            _audioLoop = Task.Run(() => AudioLoopAsync(_abort.Token));
            _notificationLoop = Task.Run(() => NotificationLoopAsync(_abort.Token));
            _logger.LogInformation("Plugin {Plugin} started", Name);
        }

        public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var wasRunning = _state == PluginState.Running;
            if (wasRunning)
            {
                _state = PluginState.Stopped;
            }

            _frames.Writer.TryComplete();
            _audio.Writer.TryComplete();
            _notifications.Writer.TryComplete();

            if (!wasRunning)
            {
                return;
            }

            // Let the plugin finish what is queued, but never wait for it longer than the timeout.
            var drained = Task.WhenAll(_frameLoop, _audioLoop, _notificationLoop);
            if (await Task.WhenAny(drained, Task.Delay(timeout, _timeProvider, cancellationToken)).ConfigureAwait(false) != drained)
            {
                _logger.LogWarning("Plugin {Plugin} did not finish its queued work within {Timeout}", Name, timeout);
                _abort.Cancel();
            }

            await drained.ConfigureAwait(false);

            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                stop.CancelAfter(timeout);
                await Invoke(ct => Plugin.StopAsync(ct), stop.Token, "stop").ConfigureAwait(false);
            }

            try
            {
                switch (Plugin)
                {
                    case IAsyncDisposable asyncDisposable:
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                        break;
                    case IDisposable disposable:
                        disposable.Dispose();
                        break;
                }
            }
            catch (Exception ex)
            {
                RecordError(ex, "dispose");
            }

            _logger.LogInformation("Plugin {Plugin} stopped", Name);
        }

        public bool IsAssigned(string cameraId)
        {
            return _context.Cameras.IsAssigned(cameraId);
        }

        public void OnFrame(CameraFrame frame)
        {
            if (_frameProcessor == null || _state != PluginState.Running || !IsAssigned(frame.CameraId))
            {
                return;
            }

            if (_frameFilter == FrameFilter.KeyFramesOnly && !frame.Frame.IsKeyFrame)
            {
                return;
            }

            _frames.Writer.TryWrite(frame);
        }

        public void OnStateChanged(CameraStateChange change)
        {
            if (_stateObserver != null && IsAssigned(change.CameraId))
            {
                Enqueue(ct => _stateObserver.OnCameraStateChangedAsync(change, ct));
            }
        }

        public void OnSegmentCompleted(RecordingSegment segment)
        {
            if (_segmentObserver != null && IsAssigned(segment.CameraId))
            {
                Enqueue(ct => _segmentObserver.OnSegmentCompletedAsync(segment, ct));
            }
        }

        public void OnCameraEvent(CameraEvent cameraEvent)
        {
            if (_cameraEventObserver != null && IsAssigned(cameraEvent.CameraId))
            {
                Enqueue(ct => _cameraEventObserver.OnCameraEventAsync(cameraEvent, ct));
            }
        }

        public void OnAudio(CameraAudio audio)
        {
            if (_audioProcessor != null && _state == PluginState.Running && IsAssigned(audio.CameraId))
            {
                _audio.Writer.TryWrite(audio);
            }
        }

        public void OnAnalytics(AnalyticsFrame frame)
        {
            if (_analyticsObserver != null && IsAssigned(frame.CameraId))
            {
                Enqueue(ct => _analyticsObserver.OnAnalyticsAsync(frame, ct));
            }
        }

        public void OnPluginEvent(PluginEvent pluginEvent)
        {
            // Plugins never receive their own events, and only events of cameras assigned to them.
            if (_eventObserver == null
                || string.Equals(pluginEvent.Source, Name, StringComparison.OrdinalIgnoreCase)
                || (pluginEvent.CameraId != null && !IsAssigned(pluginEvent.CameraId)))
            {
                return;
            }

            Enqueue(ct => _eventObserver.OnPluginEventAsync(pluginEvent, ct));
        }

        public void CountPublishedEvent()
        {
            Interlocked.Increment(ref _eventsPublished);
        }

        public PluginStatus GetStatus()
        {
            var capabilities = new List<string>();
            if (_frameProcessor != null) capabilities.Add(nameof(IFrameProcessor));
            if (_stateObserver != null) capabilities.Add(nameof(ICameraStateObserver));
            if (_segmentObserver != null) capabilities.Add(nameof(ISegmentObserver));
            if (_eventObserver != null) capabilities.Add(nameof(IPluginEventObserver));
            if (_cameraEventObserver != null) capabilities.Add(nameof(ICameraEventObserver));
            if (_analyticsObserver != null) capabilities.Add(nameof(IAnalyticsObserver));
            if (_audioProcessor != null) capabilities.Add(nameof(IAudioProcessor));

            lock (_sync)
            {
                return new PluginStatus
                {
                    Name = Name,
                    State = _state,
                    Cameras = Registration.Cameras,
                    Capabilities = capabilities,
                    FramesDelivered = Interlocked.Read(ref _framesDelivered),
                    FramesDropped = Interlocked.Read(ref _framesDropped),
                    AudioDelivered = Interlocked.Read(ref _audioDelivered),
                    AudioDropped = Interlocked.Read(ref _audioDropped),
                    NotificationsDelivered = Interlocked.Read(ref _notificationsDelivered),
                    NotificationsDropped = Interlocked.Read(ref _notificationsDropped),
                    EventsPublished = Interlocked.Read(ref _eventsPublished),
                    Errors = Interlocked.Read(ref _errors),
                    LastError = _lastError,
                    LastErrorAt = _lastErrorAt
                };
            }
        }

        #endregion

        #region Private Methods

        private void Enqueue(Func<CancellationToken, Task> notification)
        {
            if (_state == PluginState.Running)
            {
                _notifications.Writer.TryWrite(notification);
            }
        }

        private async Task FrameLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await Complete(StartFrame(frame, cancellationToken), cancellationToken, "frame").ConfigureAwait(false))
                    {
                        Interlocked.Increment(ref _framesDelivered);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task AudioLoopAsync(CancellationToken cancellationToken)
        {
            if (_audioProcessor == null)
            {
                return;
            }

            try
            {
                await foreach (var audio in _audio.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await Complete(StartAudio(audio, cancellationToken), cancellationToken, "audio").ConfigureAwait(false))
                    {
                        Interlocked.Increment(ref _audioDelivered);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private async Task NotificationLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var notification in _notifications.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await Invoke(notification, cancellationToken, "notification").ConfigureAwait(false))
                    {
                        Interlocked.Increment(ref _notificationsDelivered);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        // Frames and audio arrive many times a second per camera and plugin: no closures or delegates, and a plugin
        // that finishes synchronously costs no Task and no async state machine.

        private ValueTask StartFrame(CameraFrame frame, CancellationToken cancellationToken)
        {
            try
            {
                return _frameProcessor.ProcessFrameAsync(frame, cancellationToken);
            }
            catch (Exception ex)
            {
                return ValueTask.FromException(ex);
            }
        }

        private ValueTask StartAudio(CameraAudio audio, CancellationToken cancellationToken)
        {
            try
            {
                return _audioProcessor.ProcessAudioAsync(audio, cancellationToken);
            }
            catch (Exception ex)
            {
                return ValueTask.FromException(ex);
            }
        }

        private ValueTask<bool> Complete(ValueTask work, CancellationToken cancellationToken, string what)
        {
            if (work.IsCompletedSuccessfully)
            {
                work.GetAwaiter().GetResult();
                return new ValueTask<bool>(true);
            }

            return AwaitAsync(work, cancellationToken, what);
        }

        private async ValueTask<bool> AwaitAsync(ValueTask work, CancellationToken cancellationToken, string what)
        {
            try
            {
                await work.ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                RecordError(ex, what);
                return false;
            }
        }

        private async Task<bool> Invoke(Func<CancellationToken, Task> callback, CancellationToken cancellationToken, string what)
        {
            try
            {
                await callback(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception ex)
            {
                RecordError(ex, what);
                return false;
            }
        }

        private void RecordError(Exception ex, string what)
        {
            Interlocked.Increment(ref _errors);

            lock (_sync)
            {
                _lastError = $"{what}: {ex.Message}";
                _lastErrorAt = _timeProvider.GetUtcNow();
            }

            _logger.LogError(ex, "Plugin {Plugin} failed during {What}", Name, what);
        }

        #endregion
    }
}
