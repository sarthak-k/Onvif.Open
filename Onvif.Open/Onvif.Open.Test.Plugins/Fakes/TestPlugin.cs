// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Test.Plugins.Fakes
{
    /// <summary>
    /// Configurable plugin implementing every capability and recording what it received.
    /// </summary>
    internal class TestPlugin : IFrameProcessor, ICameraStateObserver, ISegmentObserver, IPluginEventObserver, ICameraEventObserver, IAsyncDisposable
    {
        public TestPlugin(string name, FrameFilter frameFilter = FrameFilter.AllFrames)
        {
            Name = name;
            FrameFilter = frameFilter;
        }

        public string Name { get; }
        public FrameFilter FrameFilter { get; }
        public IPluginContext Context { get; private set; }

        public ConcurrentQueue<CameraFrame> Frames { get; } = new ConcurrentQueue<CameraFrame>();
        public ConcurrentQueue<CameraStateChange> States { get; } = new ConcurrentQueue<CameraStateChange>();
        public ConcurrentQueue<RecordingSegment> Segments { get; } = new ConcurrentQueue<RecordingSegment>();
        public ConcurrentQueue<PluginEvent> Events { get; } = new ConcurrentQueue<PluginEvent>();
        public ConcurrentQueue<CameraEvent> CameraEvents { get; } = new ConcurrentQueue<CameraEvent>();

        public Task OnCameraEventAsync(CameraEvent cameraEvent, CancellationToken cancellationToken)
        {
            CameraEvents.Enqueue(cameraEvent);
            return Task.CompletedTask;
        }

        public bool Stopped { get; private set; }
        public bool Disposed { get; private set; }

        /// <summary>When set, every frame callback waits for this task (simulates slow analytics).</summary>
        public Task FrameGate { get; set; } = Task.CompletedTask;

        public Func<CameraFrame, Task> OnFrame { get; set; }
        public Exception ThrowOnStart { get; set; }
        public bool ThrowOnEveryOtherFrame { get; set; }
        public bool HangOnStop { get; set; }

        public virtual Task StartAsync(IPluginContext context, CancellationToken cancellationToken)
        {
            if (ThrowOnStart != null)
            {
                throw ThrowOnStart;
            }

            Context = context;
            return Task.CompletedTask;
        }

        public async ValueTask ProcessFrameAsync(CameraFrame frame, CancellationToken cancellationToken)
        {
            await FrameGate.WaitAsync(cancellationToken);

            if (ThrowOnEveryOtherFrame && Frames.Count % 2 == 1)
            {
                Frames.Enqueue(frame);
                throw new InvalidOperationException("analytics crashed");
            }

            Frames.Enqueue(frame);

            if (OnFrame != null)
            {
                await OnFrame(frame);
            }
        }

        public Task OnCameraStateChangedAsync(CameraStateChange change, CancellationToken cancellationToken)
        {
            States.Enqueue(change);
            return Task.CompletedTask;
        }

        public Task OnSegmentCompletedAsync(RecordingSegment segment, CancellationToken cancellationToken)
        {
            Segments.Enqueue(segment);
            return Task.CompletedTask;
        }

        public Task OnPluginEventAsync(PluginEvent pluginEvent, CancellationToken cancellationToken)
        {
            Events.Enqueue(pluginEvent);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (HangOnStop)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            Stopped = true;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>A plugin that only observes camera states (no frames).</summary>
    internal sealed class StateOnlyPlugin : ICameraStateObserver
    {
        public string Name => "state-only";
        public ConcurrentQueue<CameraStateChange> States { get; } = new ConcurrentQueue<CameraStateChange>();

        public Task StartAsync(IPluginContext context, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OnCameraStateChangedAsync(CameraStateChange change, CancellationToken cancellationToken)
        {
            States.Enqueue(change);
            return Task.CompletedTask;
        }
    }
}
