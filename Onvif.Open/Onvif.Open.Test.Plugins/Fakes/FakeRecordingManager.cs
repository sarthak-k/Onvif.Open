// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Test.Plugins.Fakes
{
    /// <summary>
    /// Recording manager whose frames, state changes and segments are raised by the test.
    /// </summary>
    internal sealed class FakeRecordingManager : IRecordingManager
    {
        private readonly List<string> _cameras;

        public FakeRecordingManager(params string[] cameras)
        {
            _cameras = cameras.ToList();
        }

        public ConcurrentQueue<string> Calls { get; } = new ConcurrentQueue<string>();

        public event EventHandler<RecordingSegment> SegmentCompleted;
        public event EventHandler<CameraFrame> FrameReceived;
        public event EventHandler<CameraStateChange> CameraStateChanged;
        public event EventHandler<CameraEvent> CameraEventReceived;
        public event EventHandler<Onvif.Open.Core.Analytics.AnalyticsFrame> AnalyticsReceived;

        public void RaiseAnalytics(Onvif.Open.Core.Analytics.AnalyticsFrame frame) => AnalyticsReceived?.Invoke(this, frame);

        public void RaiseCameraEvent(string cameraId, string topic, string isMotion)
        {
            CameraEventReceived?.Invoke(this, new CameraEvent(cameraId, topic, DateTimeOffset.UtcNow, "Changed", null, null,
                new Dictionary<string, string> { ["IsMotion"] = isMotion }));
        }

        public void AddCamera(string cameraId, ICamera camera, CameraRecordingOptions options) => throw new NotSupportedException();
        public void AddCamera(string cameraId, Func<CancellationToken, Task<ILiveVideoStream>> streamFactory, CameraRecordingOptions options, Func<CancellationToken, IAsyncEnumerable<CameraEvent>> eventSource = null, Func<CancellationToken, IAsyncEnumerable<Onvif.Open.Core.Analytics.AnalyticsFrame>> analyticsSource = null) => throw new NotSupportedException();

        public void TriggerRecording(string cameraId, TimeSpan duration)
        {
            Calls.Enqueue($"trigger:{cameraId}:{duration.TotalSeconds}");
        }

        public bool HasSubscribers => FrameReceived != null;

        public static VideoFrame Frame(int index, bool keyFrame)
        {
            return new VideoFrame(VideoCodec.H264, new byte[] { 0, 0, 0, 1, keyFrame ? (byte)0x65 : (byte)0x41, (byte)index }, keyFrame, (uint)index * 3600, TimeSpan.FromMilliseconds(index * 40));
        }

        public void RaiseFrame(string cameraId, int index, bool keyFrame)
        {
            FrameReceived?.Invoke(this, new CameraFrame(cameraId, Frame(index, keyFrame), DateTimeOffset.UtcNow));
        }

        public void RaiseState(string cameraId, RecordingState previous, RecordingState state, string error = null)
        {
            CameraStateChanged?.Invoke(this, new CameraStateChange(cameraId, previous, state, error, DateTimeOffset.UtcNow));
        }

        public void RaiseSegment(string cameraId, string fileName)
        {
            SegmentCompleted?.Invoke(this, new RecordingSegment(cameraId, "/r/" + fileName, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 188, false));
        }

        public void AddCamera(string cameraId, ICamera camera, string profileToken = null, StreamTransport transport = StreamTransport.Rtsp, IRotationPolicy rotationPolicy = null) => throw new NotSupportedException();
        public void AddCamera(string cameraId, Func<CancellationToken, Task<ILiveVideoStream>> streamFactory, IRotationPolicy rotationPolicy = null) => throw new NotSupportedException();
        public Task<bool> RemoveCameraAsync(string cameraId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StartCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            Calls.Enqueue("start:" + cameraId);
            return Task.CompletedTask;
        }

        public Task StopCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            Calls.Enqueue("stop:" + cameraId);
            return Task.CompletedTask;
        }

        public IReadOnlyList<CameraRecordingStatus> GetStatus()
        {
            return _cameras.Select(c => new CameraRecordingStatus { CameraId = c }).ToList();
        }

        public IReadOnlyList<RecordingSegment> GetSegments(string cameraId = null)
        {
            return new[] { new RecordingSegment(cameraId ?? "cam", "/r/x.ts", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1, false) };
        }

        public Task<System.IO.Stream> OpenSegmentAsync(RecordingSegment segment, CancellationToken cancellationToken = default) => Task.FromResult<System.IO.Stream>(new System.IO.MemoryStream(new byte[] { 1, 2, 3 }));
        public Task<bool> DeleteSegmentAsync(RecordingSegment segment, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public StorageInformation GetStorageInformation() => new StorageInformation("memory", null);
        public Task<IReadOnlyList<RecordingSegment>> ApplyRetentionAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RecordingSegment>>(Array.Empty<RecordingSegment>());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
