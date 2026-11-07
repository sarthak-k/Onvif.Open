// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.Replay;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Replay;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Replay.Implementation.Builder;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Replay
{
    public class CameraRecordingsTest
    {
        #region Private Variables

        private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 5, 8, 15, 0, TimeSpan.Zero);
        private static readonly byte[] Idr = new byte[] { 0x65, 0x88, 0x84, 0x00 };
        private static readonly byte[] Slice = { 0x41, 0x9A, 0x02 };

        #endregion

        #region Private Methods

        private static ICameraRecordings Recordings(FakeOnvifServer device)
        {
            var camera = new TestCameraInformation(device.DeviceServiceUri);
            return CameraRecordings.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build());
        }

        /// <summary>A device replaying 1 key frame + 3 frames per second, for <paramref name="seconds"/> seconds from <see cref="Start"/>.</summary>
        private static FakeRtspServer ReplayServer(int seconds, bool closeAtEnd = true)
        {
            var server = new FakeRtspServer { CloseAfterPackets = closeAtEnd };
            ushort sequence = 1;

            for (var frame = 0; frame < seconds * 4; frame++)
            {
                var time = Start.AddMilliseconds(frame * 250);
                server.Packets.Add(RtpPacketFactory.CreateReplay(sequence++, (uint)(frame * 22500), true, frame % 4 == 0 ? Idr : Slice, time, cleanPoint: frame % 4 == 0));
            }

            return server;
        }

        private static async Task<List<VideoFrame>> ReadAllAsync(ILiveVideoStream stream)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var frames = new List<VideoFrame>();

                await foreach (var frame in stream.ReadFramesAsync(timeout.Token))
                {
                    frames.Add(frame);
                }

                return frames;
            }
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task RecordingsShouldBeFoundAcrossResultPages()
        {
            using (var device = new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")))
            {
                var recordings = await Recordings(device).GetRecordingsAsync();

                Assert.Equal(new[] { "Rec_1", "Rec_2" }, recordings.Select(r => r.Token));
                Assert.Equal(1, device.EndSearches);

                var lobby = recordings[0];
                Assert.Equal("Lobby", lobby.SourceName);
                Assert.Equal("Lobby", lobby.Location);
                Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), lobby.Earliest);
                Assert.Equal(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), lobby.Latest);
                Assert.Equal(DeviceRecordingStatus.Recording, lobby.Status);
                Assert.Equal(new[] { DeviceTrackType.Video, DeviceTrackType.Audio }, lobby.Tracks.Select(t => t.Type));
                Assert.Equal(DeviceRecordingStatus.Stopped, recordings[1].Status);
            }
        }

        [Fact]
        public async Task SummaryAndSingleRecordingShouldBeRead()
        {
            using (var device = new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")))
            {
                var summary = await Recordings(device).GetSummaryAsync();
                Assert.Equal(2, summary.RecordingCount);
                Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), summary.DataFrom);
                Assert.Equal(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), summary.DataUntil);

                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var search = RecordingSearchServiceBuilder.GetBuilder().SetCamera(camera).SetServiceUri(device.SearchServiceUri).Build();
                Assert.Equal("Lobby", (await search.GetRecordingAsync("Rec_1")).SourceName);
                Assert.Null(await search.GetRecordingAsync("Unknown"));
            }
        }

        [Fact]
        public async Task ReplayShouldRequestTheRangeAndStampFramesWithTheirRecordingTime()
        {
            await using (var rtsp = ReplayServer(seconds: 2))
            using (var device = new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")) { ReplayRtspUri = rtsp.StreamUri })
            {
                var stream = await Recordings(device).StartReplayAsync("Rec_2", Start, new ReplayOptions { Speed = 2 });
                List<VideoFrame> frames;

                await using (stream)
                {
                    // The stream ends when the device closes the connection at the end of the recording.
                    frames = await ReadAllAsync(stream);
                }

                Assert.Equal("Rec_2", device.LastReplayRecording);
                Assert.Equal(8, frames.Count);
                Assert.True(frames[0].IsKeyFrame);
                Assert.Equal(Start, frames[0].CaptureTime);
                Assert.Equal(Start.AddMilliseconds(1750), frames[7].CaptureTime);

                var headers = rtsp.RequestHeaders.ToList();
                Assert.All(new[] { "DESCRIBE", "SETUP", "PLAY" }, method => Assert.Contains($"{method} Require: onvif-replay", headers));
                Assert.Contains("PLAY Range: clock=20261105T081500.000Z-", headers);
                Assert.Contains("PLAY Scale: 2.0", headers);
                Assert.DoesNotContain(headers, h => h.StartsWith("PLAY Rate-Control", StringComparison.Ordinal));
            }
        }

        [Fact]
        public async Task ReplayShouldStopAtTheRequestedEnd()
        {
            await using (var rtsp = ReplayServer(seconds: 3, closeAtEnd: false))
            using (var device = new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")) { ReplayRtspUri = rtsp.StreamUri })
            {
                var to = Start.AddSeconds(1);
                var stream = await Recordings(device).StartReplayAsync("Rec_2", Start, new ReplayOptions { To = to, RealTime = false });
                List<VideoFrame> frames;

                await using (stream)
                {
                    frames = await ReadAllAsync(stream);
                }

                // Frames at 0, 250, 500, 750 and 1000 ms.
                Assert.Equal(5, frames.Count);
                Assert.All(frames, f => Assert.True(f.CaptureTime <= to));

                var headers = rtsp.RequestHeaders.ToList();
                Assert.Contains("PLAY Range: clock=20261105T081500.000Z-20261105T081501.000Z", headers);
                Assert.Contains("PLAY Rate-Control: no", headers);
            }
        }

        [Fact]
        public async Task SlowConsumersShouldGetEveryReplayedFrame()
        {
            // Live video drops frames for slow readers; replay (export) must not.
            await using (var rtsp = ReplayServer(seconds: 20))
            using (var device = new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")) { ReplayRtspUri = rtsp.StreamUri })
            {
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var stream = await Onvif.Open.Streaming.Implementation.Builder.LiveVideoStreamBuilder.GetBuilder()
                    .SetStreamUri(rtsp.StreamUri)
                    .SetCamera(camera)
                    .SetFrameBufferCapacity(4)
                    .SetReplay(Start, new ReplayOptions { RealTime = false })
                    .BuildAsync();

                await using (stream)
                {
                    await Task.Delay(300);
                    var frames = await ReadAllAsync(stream);
                    Assert.Equal(80, frames.Count);
                }
            }
        }

        [Fact]
        public async Task CopyToShouldExportTheReplayedVideo()
        {
            await using (var rtsp = ReplayServer(seconds: 1))
            using (var device = new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")) { ReplayRtspUri = rtsp.StreamUri })
            using (var output = new MemoryStream())
            {
                await using (var stream = await Recordings(device).StartReplayAsync("Rec_2", Start, new ReplayOptions { RealTime = false }))
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    await stream.CopyToAsync(output, timeout.Token);
                }

                Assert.True(output.Length > 0);
            }
        }

        [Fact]
        public async Task CamerasWithoutProfileGShouldReportNotSupported()
        {
            using (var device = new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream")) { HasProfileG = false })
            {
                var recordings = Recordings(device);

                await Assert.ThrowsAsync<NotSupportedException>(() => recordings.GetRecordingsAsync());
                await Assert.ThrowsAsync<NotSupportedException>(() => recordings.StartReplayAsync("Rec_1", Start));
            }
        }

        [Fact]
        public void InvalidReplayOptionsShouldBeRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayOptions { To = Start }.Validate(Start));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayOptions { Speed = 0 }.Validate(Start));
            Assert.Throws<NotSupportedException>(() =>
                Onvif.Open.Streaming.Implementation.Builder.LiveVideoStreamBuilder.GetBuilder()
                    .SetStreamUri(new Uri("rtsp://127.0.0.1:1/replay"))
                    .SetTransport(Onvif.Open.Core.Operations.StreamTransport.Udp)
                    .SetReplay(Start)
                    .BuildAsync().GetAwaiter().GetResult());
        }

        #endregion
    }
}
