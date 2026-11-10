// Sarthak Killedar
// 2026-11-10

using System;
using System.Linq;
using System.Threading.Tasks;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recorder.Hosting;
using Onvif.Open.Recording.Implementation.Builder;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Smaller streams for live grids: opened from the camera when the first tile asks, shared by every tile,
    /// reconnected when they drop, and closed once nobody watches.
    /// </summary>
    public sealed class SubStreamTest
    {
        private static FakeLiveVideoStream RealVideo(int frames)
        {
            var stream = new FakeLiveVideoStream();
            foreach (var frame in RealMedia.Frames.OfType<VideoFrame>().Take(frames))
            {
                stream.PushFrame(frame);
            }

            return stream;
        }

        [SkippableFact]
        public async Task GridsShouldGetTheSmallerStreamWhileTheyWatchIt()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            using (var temp = new TempDirectory())
            {
                var clock = new ManualTimeProvider();
                var sub = new FakeStreamSource().Add(RealVideo(30).End()).Add(RealVideo(30));
                await using (var manager = RecordingManagerBuilder.GetBuilder().SetStorageDirectory(temp.Path).Build())
                {
                    manager.AddCamera("hall", new FakeStreamSource().ConnectAsync);
                    manager.AddCamera("dock", new FakeStreamSource().ConnectAsync);
                    using (var live = new LiveViewService(manager, new LiveViewOptions { IdleTimeout = TimeSpan.FromMinutes(1) }, clock, id => id == "hall" ? sub.ConnectAsync : null))
                    {
                        // A camera without a smaller stream: grids get the main one.
                        var main = live.WatchMp4("dock", true, out var dockSub);
                        Assert.False(dockSub);
                        Assert.Same(main, live.WatchMp4("dock"));

                        // The first tile opens the smaller stream; the next ones share it.
                        var grid = live.WatchMp4("hall", true, out var isSub);
                        Assert.True(isSub);
                        Assert.NotSame(live.WatchMp4("hall"), grid);
                        Assert.True(await grid.WaitUntilReadyAsync(TimeSpan.FromSeconds(10)));
                        Assert.Equal(Onvif.Open.Recording.Implementation.Mp4.Mp4LiveStates.Streaming, grid.State);
                        Assert.Same(grid, live.WatchMp4("hall", true, out _));
                        Assert.Equal(1, live.OpenSubStreamCount);

                        // It dropped (the first connection ended): reconnected, still the same stream for the tiles.
                        await Eventually.True(() => sub.Connects >= 2, "the smaller stream reconnected");
                        await Eventually.True(() => live.SubStreamState("hall") == SubStreamStates.Streaming, "it streams again");

                        // Watched: kept open, however long.
                        using (grid.Watch())
                        {
                            clock.Advance(TimeSpan.FromMinutes(5));
                            live.RemoveIdle();
                            Assert.Equal(1, live.OpenSubStreamCount);
                        }

                        // Nobody watches it any more: closed, until a tile asks again.
                        clock.Advance(TimeSpan.FromMinutes(2));
                        live.RemoveIdle();
                        Assert.Equal(0, live.OpenSubStreamCount);
                        Assert.Equal(SubStreamStates.Closed, live.SubStreamState("hall"));
                        var connects = sub.Connects;
                        await Task.Delay(1500);
                        Assert.Equal(connects, sub.Connects);
                    }
                }
            }
        }

        [Fact]
        public async Task ARemovedCameraShouldCloseItsSmallerStream()
        {
            using (var temp = new TempDirectory())
            {
                var clock = new ManualTimeProvider();
                var stream = new FakeLiveVideoStream();
                var sub = new FakeStreamSource().Add(stream);
                await using (var manager = RecordingManagerBuilder.GetBuilder().SetStorageDirectory(temp.Path).Build())
                {
                    manager.AddCamera("hall", new FakeStreamSource().ConnectAsync);
                    using (var live = new LiveViewService(manager, new LiveViewOptions(), clock, _ => sub.ConnectAsync))
                    {
                        var grid = live.WatchMp4("hall", true, out _);
                        var viewer = grid.Watch();
                        await Eventually.True(() => sub.Connects == 1, "the smaller stream was opened");

                        await manager.RemoveCameraAsync("hall");
                        live.RemoveIdle();

                        Assert.Equal(0, live.OpenSubStreamCount);
                        await Eventually.True(() => stream.Disposed, "the camera connection was closed");
                        Assert.True(viewer.Fragments.Completion.IsCompleted || !viewer.Fragments.TryPeek(out _));
                        Assert.Null(live.WatchMp4("hall", true, out _));
                    }
                }
            }
        }
    }
}
