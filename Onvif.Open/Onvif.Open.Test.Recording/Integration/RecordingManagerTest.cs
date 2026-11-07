// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Builder;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Recording.Implementation.Policy;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    public class RecordingManagerTest
    {
        #region Private Methods

        private static RecordingManagerBuilder Builder(TempDirectory temp)
        {
            return RecordingManagerBuilder.GetBuilder()
                .SetStorageDirectory(temp.Path)
                .SetRetentionPolicy(RetentionPolicies.KeepAll)
                .SetReconnectDelay(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50))
                .SetStallTimeout(TimeSpan.FromSeconds(5));
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task ShouldRotateAtKeyFramesAndSkipUntilFirstKeyFrame()
        {
            using (var temp = new TempDirectory())
            {
                var completed = new ConcurrentQueue<RecordingSegment>();
                var stream = new FakeLiveVideoStream();
                // 2 frames before the first key frame are dropped, then 3 GOPs of 5 frames.
                stream.PushFrame(FakeLiveVideoStream.Frame(100, false)).PushFrame(FakeLiveVideoStream.Frame(101, false));
                stream.Push(15, gop: 5);

                await using (var manager = Builder(temp).SetRotationPolicy(RotationPolicies.When(s => s.FrameCount >= 5)).Build())
                {
                    manager.SegmentCompleted += (_, segment) => completed.Enqueue(segment);
                    manager.AddCamera("cam1", new FakeStreamSource().Add(stream).ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => completed.Count >= 2 && manager.GetStatus()[0].FramesRecorded == 15, "two segments are completed");

                    var status = manager.GetStatus().Single();
                    Assert.Equal(RecordingState.Recording, status.State);
                    Assert.Equal(VideoCodec.H264, status.Codec);
                    Assert.NotNull(status.CurrentSegment);

                    var segments = manager.GetSegments("cam1");
                    Assert.Equal(3, segments.Count);
                    Assert.True(segments.Last().IsActive);
                    Assert.All(segments, s => Assert.EndsWith(".ts", s.Path));
                    Assert.All(completed, c => Assert.True(c.Size > 0 && c.Size % 188 == 0));
                }

                // Stopping closes the last segment.
                Assert.Equal(3, completed.Count);
            }
        }

        [Fact]
        public async Task ShouldReconnectWhenStreamEndsOrFails()
        {
            using (var temp = new TempDirectory())
            {
                var source = new FakeStreamSource()
                    .Add(new FakeLiveVideoStream().Push(5).End())
                    .Add(new FakeLiveVideoStream().Push(5).End(new IOException("connection reset")))
                    .Add(new FakeLiveVideoStream().Push(5));

                await using (var manager = Builder(temp).Build())
                {
                    manager.AddCamera("cam1", source.ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => manager.GetStatus()[0].FramesRecorded == 15, "all three sessions were recorded");

                    var status = manager.GetStatus()[0];
                    Assert.Equal(3, source.Connects);
                    Assert.Equal("connection reset", status.LastError);
                    Assert.Equal(3, manager.GetSegments("cam1").Count);
                }
            }
        }

        [Fact]
        public async Task ShouldKeepRetryingWhileCameraIsOffline()
        {
            using (var temp = new TempDirectory())
            {
                var source = new FakeStreamSource();

                await using (var manager = Builder(temp).Build())
                {
                    manager.AddCamera("cam1", source.ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => source.Connects >= 3, "the recorder retried");

                    var status = manager.GetStatus()[0];
                    Assert.Contains(status.State, new[] { RecordingState.Connecting, RecordingState.Reconnecting });
                    Assert.Equal("camera offline", status.LastError);

                    await manager.StopAsync();
                    Assert.Equal(RecordingState.Stopped, manager.GetStatus()[0].State);
                }
            }
        }

        [Fact]
        public async Task StalledStreamShouldBeReconnected()
        {
            using (var temp = new TempDirectory())
            {
                var stalled = new FakeLiveVideoStream().Push(5);
                var source = new FakeStreamSource().Add(stalled).Add(new FakeLiveVideoStream().Push(5));

                await using (var manager = Builder(temp).SetStallTimeout(TimeSpan.FromMilliseconds(300)).Build())
                {
                    manager.AddCamera("cam1", source.ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => manager.GetStatus()[0].FramesRecorded == 10, "the second session was recorded");

                    Assert.True(stalled.Disposed);
                    Assert.Contains("No video received", manager.GetStatus()[0].LastError);
                }
            }
        }

        [Fact]
        public async Task ShouldRecordMultipleCamerasIntoSeparateFolders()
        {
            using (var temp = new TempDirectory())
            {
                await using (var manager = Builder(temp).SetFormat(SegmentFormats.ElementaryStream).Build())
                {
                    manager.AddCamera("front-door", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(10)).ConnectAsync);
                    manager.AddCamera("garage", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(20)).ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => manager.GetStatus().Sum(s => s.FramesRecorded) == 30, "both cameras recorded");

                    Assert.Equal(new[] { "front-door", "garage" }, manager.GetStatus().Select(s => s.CameraId));
                    Assert.Single(manager.GetSegments("front-door"));
                    Assert.Single(manager.GetSegments("garage"));
                    Assert.True(Directory.Exists(Path.Combine(temp.Path, "front-door")));
                    Assert.EndsWith(".h264", manager.GetSegments("garage")[0].Path);
                }
            }
        }

        [Fact]
        public async Task RetentionShouldDeleteOldSegmentsButNeverTheActiveOne()
        {
            using (var temp = new TempDirectory())
            {
                var stream = new FakeLiveVideoStream();

                await using (var manager = Builder(temp)
                    .SetRotationPolicy(RotationPolicies.When(s => s.FrameCount >= 5))
                    .SetRetentionPolicy(RetentionPolicies.MaxSizePerCamera(1))
                    .Build())
                {
                    manager.AddCamera("cam1", new FakeStreamSource().Add(stream).ConnectAsync);
                    await manager.StartAsync();

                    stream.Push(25, gop: 5);
                    await Eventually.True(() => manager.GetStatus()[0].FramesRecorded == 25, "all frames were recorded");
                    await manager.ApplyRetentionAsync();

                    // Every completed segment exceeds the 1 byte budget: only the active one survives.
                    var remaining = Assert.Single(manager.GetSegments());
                    Assert.True(remaining.IsActive);
                    Assert.Equal(4, manager.GetStatus()[0].SegmentsCompleted);
                }
            }
        }

        [Fact]
        public async Task ExistingRecordingsShouldBeSubjectToRetention()
        {
            using (var temp = new TempDirectory())
            {
                var oldDay = Path.Combine(temp.Path, "cam1", "2020-01-01");
                Directory.CreateDirectory(oldDay);
                var oldFile = Path.Combine(oldDay, "cam1_20200101T000000000Z.ts");
                File.WriteAllBytes(oldFile, new byte[188]);
                File.SetLastWriteTimeUtc(oldFile, new DateTime(2020, 1, 1, 0, 10, 0, DateTimeKind.Utc));

                await using (var manager = Builder(temp).SetRetentionPolicy(RetentionPolicies.KeepFor(TimeSpan.FromDays(30))).Build())
                {
                    var deleted = await manager.ApplyRetentionAsync();

                    Assert.Equal(oldFile, Assert.Single(deleted).Path);
                    Assert.False(File.Exists(oldFile));
                }
            }
        }

        [Fact]
        public async Task CamerasCanBeStoppedStartedAndRemoved()
        {
            using (var temp = new TempDirectory())
            {
                var source = new FakeStreamSource().Add(new FakeLiveVideoStream().Push(5)).Add(new FakeLiveVideoStream().Push(5));

                await using (var manager = Builder(temp).Build())
                {
                    manager.AddCamera("cam1", source.ConnectAsync);
                    await manager.StartAsync();
                    await Eventually.True(() => manager.GetStatus()[0].FramesRecorded == 5, "first session recorded");

                    await manager.StopCameraAsync("cam1");
                    Assert.Equal(RecordingState.Stopped, manager.GetStatus()[0].State);
                    Assert.Null(manager.GetStatus()[0].CurrentSegment);

                    await manager.StartCameraAsync("cam1");
                    await Eventually.True(() => manager.GetStatus()[0].FramesRecorded == 10, "second session recorded");

                    Assert.True(await manager.RemoveCameraAsync("cam1"));
                    Assert.False(await manager.RemoveCameraAsync("cam1"));
                    Assert.Empty(manager.GetStatus());
                    Assert.Equal(2, manager.GetSegments("cam1").Count);
                    await Assert.ThrowsAsync<KeyNotFoundException>(() => manager.StopCameraAsync("cam1"));
                }
            }
        }

        [Fact]
        public async Task CameraAddedAfterStartShouldStartImmediately()
        {
            using (var temp = new TempDirectory())
            {
                await using (var manager = Builder(temp).Build())
                {
                    await manager.StartAsync();
                    manager.AddCamera("late", new FakeStreamSource().Add(new FakeLiveVideoStream().Push(5)).ConnectAsync);

                    await Eventually.True(() => manager.GetStatus()[0].FramesRecorded == 5, "the late camera recorded");
                }
            }
        }

        [Fact]
        public async Task UnsupportedCodecShouldBeReported()
        {
            using (var temp = new TempDirectory())
            {
                await using (var manager = Builder(temp).Build())
                {
                    manager.AddCamera("mjpeg", _ => Task.FromResult<ILiveVideoStream>(new FakeLiveVideoStream(VideoCodec.Unknown).Push(5)));
                    await manager.StartAsync();

                    await Eventually.True(() => manager.GetStatus()[0].LastError != null, "the error was reported");
                    Assert.Contains("cannot be recorded", manager.GetStatus()[0].LastError);
                    Assert.Empty(manager.GetSegments());
                }
            }
        }

        [Fact]
        public async Task OnvifCameraShouldUseProfileAndTransport()
        {
            using (var temp = new TempDirectory())
            {
                var camera = new StubCamera(new FakeLiveVideoStream().Push(5));

                await using (var manager = Builder(temp).Build())
                {
                    manager.AddCamera("onvif", camera, "Profile_2", StreamTransport.Udp);
                    await manager.StartAsync();

                    await Eventually.True(() => manager.GetStatus()[0].FramesRecorded == 5, "the ONVIF camera recorded");
                    Assert.Equal("Profile_2", camera.Media.RequestedProfile);
                    Assert.Equal(StreamTransport.Udp, camera.Media.RequestedTransport);
                }
            }
        }

        [Fact]
        public async Task ShouldRaiseFrameAndStateChangeEvents()
        {
            using (var temp = new TempDirectory())
            {
                var source = new FakeStreamSource()
                    .Add(new FakeLiveVideoStream().Push(5).End(new IOException("camera unplugged")))
                    .Add(new FakeLiveVideoStream().Push(5));
                var states = new ConcurrentQueue<CameraStateChange>();
                var frames = new ConcurrentQueue<CameraFrame>();

                await using (var manager = Builder(temp).Build())
                {
                    manager.CameraStateChanged += (_, change) => states.Enqueue(change);
                    manager.FrameReceived += (_, frame) => frames.Enqueue(frame);
                    manager.AddCamera("cam1", source.ConnectAsync);
                    await manager.StartAsync();

                    await Eventually.True(() => frames.Count == 10 && states.Count(s => s.State == RecordingState.Recording) == 2, "both sessions were recorded");
                    await manager.StopAsync();
                }

                var sequence = states.Select(s => s.State).ToArray();
                Assert.Equal(new[]
                {
                    RecordingState.Connecting, RecordingState.Recording, RecordingState.Reconnecting,
                    RecordingState.Connecting, RecordingState.Recording, RecordingState.Stopped
                }, sequence);

                var lost = states.Single(s => s.IsConnectionLost);
                Assert.Equal("camera unplugged", lost.Error);
                Assert.Equal("cam1", lost.CameraId);
                Assert.All(states.Where(s => !s.IsConnectionLost), s => Assert.Null(s.Error));
                Assert.All(frames, f => Assert.Equal("cam1", f.CameraId));
            }
        }

        [Fact]
        public void InvalidRegistrationsShouldThrow()
        {
            using (var temp = new TempDirectory())
            {
                var manager = Builder(temp).Build();
                Func<CancellationToken, Task<ILiveVideoStream>> factory = new FakeStreamSource().ConnectAsync;

                manager.AddCamera("cam1", factory);
                Assert.Throws<ArgumentException>(() => manager.AddCamera("CAM1", factory));
                Assert.Throws<ArgumentException>(() => manager.AddCamera("../cam", factory));
                Assert.Throws<ArgumentNullException>(() => manager.AddCamera("cam2", (Func<CancellationToken, Task<ILiveVideoStream>>)null));
                Assert.Throws<InvalidOperationException>(() => RecordingManagerBuilder.GetBuilder().Build());
            }
        }

        #endregion

        private sealed class StubCamera : ICamera
        {
            public StubCamera(ILiveVideoStream stream)
            {
                Media = new StubMedia(stream);
            }

            public StubMedia Media { get; }

            public ICameraDeviceManagement Initialize() => throw new NotSupportedException();
            public ICameraInformation GetCameraInformation() => throw new NotSupportedException();
            public ICameraMedia InitializeMedia() => Media;
            public TimeSpan SynchronizeClock() => TimeSpan.Zero;
        }

        private sealed class StubMedia : ICameraMedia
        {
            private readonly ILiveVideoStream _stream;

            public StubMedia(ILiveVideoStream stream)
            {
                _stream = stream;
            }

            public string RequestedProfile { get; private set; }
            public StreamTransport RequestedTransport { get; private set; }

            public IMediaService GetMediaService() => throw new NotSupportedException();

            public Task<byte[]> GetSnapshotAsync(string profileToken = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public Task<ILiveVideoStream> StartLiveStreamAsync(string profileToken = null, StreamTransport transport = StreamTransport.Rtsp, CancellationToken cancellationToken = default)
            {
                RequestedProfile = profileToken;
                RequestedTransport = transport;
                return Task.FromResult(_stream);
            }
        }
    }
}
