// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Core.Recording;
using Onvif.Open.Plugins.Implementation.Builder;
using Onvif.Open.Test.Plugins.Fakes;
using Xunit;

namespace Onvif.Open.Test.Plugins.Unit
{
    public class PluginHostTest
    {
        #region Private Methods

        private static PluginHostBuilder Builder(FakeRecordingManager manager)
        {
            return PluginHostBuilder.GetBuilder().SetRecordingManager(manager).SetStopTimeout(TimeSpan.FromSeconds(2));
        }

        private static PluginStatus Status(IPluginHost host, string name)
        {
            return host.GetStatus().Single(s => s.Name == name);
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task FramesShouldOnlyReachAssignedCameras()
        {
            var manager = new FakeRecordingManager("room-1", "room-2");
            var roomOne = new TestPlugin("room-one");
            var all = new TestPlugin("all");

            await using (var host = Builder(manager).AddPlugin(roomOne, new[] { "room-1" }).AddPlugin(all).Build())
            {
                await host.StartAsync();

                manager.RaiseFrame("room-1", 0, true);
                manager.RaiseFrame("room-2", 1, true);

                await Eventually.True(() => all.Frames.Count == 2 && roomOne.Frames.Count == 1, "frames are delivered");
                Assert.Equal("room-1", roomOne.Frames.Single().CameraId);
                Assert.Equal(new[] { "room-1" }, Status(host, "room-one").Cameras);
                Assert.Null(Status(host, "all").Cameras);
            }
        }

        [Fact]
        public async Task KeyFramesOnlyFilterShouldSkipOtherFrames()
        {
            var manager = new FakeRecordingManager("cam");
            var plugin = new TestPlugin("keyframes", FrameFilter.KeyFramesOnly);

            await using (var host = Builder(manager).AddPlugin(plugin).Build())
            {
                await host.StartAsync();

                for (var i = 0; i < 10; i++)
                {
                    manager.RaiseFrame("cam", i, i % 5 == 0);
                }

                await Eventually.True(() => plugin.Frames.Count == 2, "key frames are delivered");
                await Task.Delay(50);
                Assert.All(plugin.Frames, f => Assert.True(f.Frame.IsKeyFrame));
            }
        }

        [Fact]
        public async Task SlowPluginShouldDropOldFramesWithoutBlockingRecordingOrOthers()
        {
            var manager = new FakeRecordingManager("cam");
            var gate = new TaskCompletionSource();
            var slow = new TestPlugin("slow") { FrameGate = gate.Task };
            var fast = new TestPlugin("fast");

            await using (var host = Builder(manager).SetFrameQueueCapacity(4).AddPlugin(slow).AddPlugin(fast).Build())
            {
                await host.StartAsync();

                var watch = Stopwatch.StartNew();
                for (var i = 0; i < 100; i++)
                {
                    manager.RaiseFrame("cam", i, false);
                }

                // Raising (= the recording thread) never waits for plugins.
                Assert.True(watch.ElapsedMilliseconds < 1000);

                // The fast plugin processes its queue while the slow one is still blocked on its first frame.
                await Eventually.True(() => Status(host, "fast").FramesDelivered + Status(host, "fast").FramesDropped == 100, "the fast plugin processed its queue");
                Assert.Equal(0, Status(host, "slow").FramesDelivered);
                Assert.Equal(99, fast.Frames.Last().Frame.Data.Span[5]);

                gate.SetResult();
                await Eventually.True(() => Status(host, "slow").FramesDelivered + Status(host, "slow").FramesDropped == 100, "the slow plugin caught up");

                var status = Status(host, "slow");
                Assert.True(status.FramesDropped >= 90, $"dropped {status.FramesDropped}");

                // The newest frames are kept, the oldest dropped.
                Assert.Equal(99, slow.Frames.Last().Frame.Data.Span[5]);
            }
        }

        [Fact]
        public async Task CallbackExceptionsShouldBeIsolated()
        {
            var manager = new FakeRecordingManager("cam");
            var flaky = new TestPlugin("flaky") { ThrowOnEveryOtherFrame = true };
            var healthy = new TestPlugin("healthy");

            await using (var host = Builder(manager).AddPlugin(flaky).AddPlugin(healthy).Build())
            {
                await host.StartAsync();

                for (var i = 0; i < 6; i++)
                {
                    manager.RaiseFrame("cam", i, false);
                }

                manager.RaiseState("cam", RecordingState.Recording, RecordingState.Reconnecting, "timeout");

                await Eventually.True(() => flaky.Frames.Count == 6 && flaky.States.Count == 1 && healthy.Frames.Count == 6, "every callback ran");

                var status = Status(host, "flaky");
                Assert.Equal(PluginState.Running, status.State);
                Assert.Equal(3, status.Errors);
                Assert.Equal(3, status.FramesDelivered);
                Assert.Contains("analytics crashed", status.LastError);
                Assert.Equal(0, Status(host, "healthy").Errors);
            }
        }

        [Fact]
        public async Task PluginFailingToStartShouldBeFaultedAndIgnored()
        {
            var manager = new FakeRecordingManager("cam");
            var broken = new TestPlugin("broken") { ThrowOnStart = new InvalidOperationException("missing model file") };
            var healthy = new TestPlugin("healthy");

            await using (var host = Builder(manager).AddPlugin(broken).AddPlugin(healthy).Build())
            {
                await host.StartAsync();
                manager.RaiseFrame("cam", 0, true);

                await Eventually.True(() => healthy.Frames.Count == 1, "the healthy plugin runs");

                var status = Status(host, "broken");
                Assert.Equal(PluginState.Faulted, status.State);
                Assert.Contains("missing model file", status.LastError);
                Assert.Empty(broken.Frames);
            }
        }

        [Fact]
        public async Task StateChangesAndSegmentsShouldBeDeliveredInOrder()
        {
            var manager = new FakeRecordingManager("cam");
            var plugin = new TestPlugin("observer");
            var stateOnly = new StateOnlyPlugin();

            await using (var host = Builder(manager).AddPlugin(plugin).AddPlugin(stateOnly).Build())
            {
                await host.StartAsync();

                manager.RaiseState("cam", RecordingState.Stopped, RecordingState.Connecting);
                manager.RaiseState("cam", RecordingState.Connecting, RecordingState.Recording);
                manager.RaiseSegment("cam", "a.ts");
                manager.RaiseState("cam", RecordingState.Recording, RecordingState.Reconnecting, "No video received for 15 seconds.");

                await Eventually.True(() => plugin.States.Count == 3 && plugin.Segments.Count == 1 && stateOnly.States.Count == 3, "notifications arrived");

                var states = plugin.States.ToArray();
                Assert.Equal(new[] { RecordingState.Connecting, RecordingState.Recording, RecordingState.Reconnecting }, states.Select(s => s.State));
                Assert.True(states[1].IsConnectionEstablished);
                Assert.True(states[2].IsConnectionLost);
                Assert.Equal("No video received for 15 seconds.", states[2].Error);
                Assert.Equal("a.ts", plugin.Segments.Single().FileName);
                Assert.Equal(new[] { nameof(ICameraStateObserver) }, Status(host, "state-only").Capabilities);
            }
        }

        [Fact]
        public async Task EventsShouldBeStoredAndForwardedToOtherPlugins()
        {
            var manager = new FakeRecordingManager("room-1", "room-2");
            var detector = new TestPlugin("detector");
            var notifier = new TestPlugin("notifier");
            var otherRoom = new TestPlugin("other-room");

            await using (var host = Builder(manager).AddPlugin(detector).AddPlugin(notifier).AddPlugin(otherRoom, new[] { "room-2" }).Build())
            {
                var published = new List<PluginEvent>();
                host.EventPublished += (_, e) => published.Add(e);
                await host.StartAsync();

                await detector.Context.PublishAsync(new PluginEvent("exam.suspicious", "room-1", "two faces", PluginEventSeverity.Critical,
                    new Dictionary<string, string> { ["confidence"] = "0.91" }));

                await Eventually.True(() => notifier.Events.Count == 1, "the notifier received the event");
                await Task.Delay(50);

                var received = notifier.Events.Single();
                Assert.Equal("detector", received.Source);
                Assert.Equal("0.91", received.Data["confidence"]);
                Assert.NotEqual(default, received.Timestamp);

                Assert.Empty(detector.Events);   // never its own events
                Assert.Empty(otherRoom.Events);  // not assigned to room-1
                Assert.Single(published);

                var stored = Assert.Single(host.GetEvents());
                Assert.Equal(received.Id, stored.Id);
                Assert.Equal(1, Status(host, "detector").EventsPublished);
                Assert.Single(host.GetEvents(new PluginEventQuery { MinimumSeverity = PluginEventSeverity.Warning }));
                Assert.Empty(host.GetEvents(new PluginEventQuery { CameraId = "room-2" }));
                Assert.Empty(host.GetEvents(new PluginEventQuery { Type = "other" }));
            }
        }

        [Fact]
        public async Task EventHistoryShouldKeepNewestEvents()
        {
            var manager = new FakeRecordingManager("cam");
            var plugin = new TestPlugin("publisher");

            await using (var host = Builder(manager).SetEventHistoryCapacity(3).AddPlugin(plugin).Build())
            {
                await host.StartAsync();

                for (var i = 0; i < 5; i++)
                {
                    await plugin.Context.PublishAsync(new PluginEvent("tick", message: i.ToString()));
                }

                Assert.Equal(new[] { "4", "3", "2" }, host.GetEvents().Select(e => e.Message));
                Assert.Equal(new[] { "4" }, host.GetEvents(new PluginEventQuery { Limit = 1 }).Select(e => e.Message));
            }
        }

        [Fact]
        public async Task PluginsShouldOnlyAccessAssignedCameras()
        {
            var manager = new FakeRecordingManager("room-1", "room-2");
            var plugin = new TestPlugin("restricted");
            var snapshots = new List<string>();

            await using (var host = Builder(manager)
                .AddPlugin(plugin, new[] { "room-1" }, new Dictionary<string, string> { ["Schedule:Start"] = "08:00" })
                .SetSnapshotProvider((id, ct) =>
                {
                    snapshots.Add(id);
                    return Task.FromResult(new byte[] { 0xFF, 0xD8 });
                })
                .Build())
            {
                await host.StartAsync();
                var context = plugin.Context;

                Assert.Equal("restricted", context.PluginName);
                Assert.Equal("08:00", context.Settings["Schedule:Start"]);
                Assert.Equal(new[] { "room-1" }, context.Cameras.GetStatus().Select(s => s.CameraId));

                await context.Cameras.StopRecordingAsync("room-1");
                await context.Cameras.StartRecordingAsync("room-1");
                Assert.Equal(new[] { "stop:room-1", "start:room-1" }, manager.Calls);
                Assert.Equal(new byte[] { 0xFF, 0xD8 }, await context.Cameras.GetSnapshotAsync("room-1"));
                Assert.Single(context.Cameras.GetSegments("room-1"));

                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Cameras.StopRecordingAsync("room-2"));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.Cameras.GetSnapshotAsync("room-2"));
                Assert.Throws<UnauthorizedAccessException>(() => context.Cameras.GetSegments("room-2"));
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.PublishAsync(new PluginEvent("x", "room-2")));
                Assert.Equal(new[] { "room-1" }, snapshots);
            }
        }

        [Fact]
        public async Task SnapshotsWithoutProviderShouldBeNotSupported()
        {
            var manager = new FakeRecordingManager("cam");
            var plugin = new TestPlugin("p");

            await using (var host = Builder(manager).AddPlugin(plugin).Build())
            {
                await host.StartAsync();
                await Assert.ThrowsAsync<NotSupportedException>(() => plugin.Context.Cameras.GetSnapshotAsync("cam"));
            }
        }

        [Fact]
        public async Task StopShouldDrainQueuesThenStopAndDisposePlugins()
        {
            var manager = new FakeRecordingManager("cam");
            var gate = new TaskCompletionSource();
            var plugin = new TestPlugin("draining") { FrameGate = gate.Task };
            var host = Builder(manager).AddPlugin(plugin).Build();
            await host.StartAsync();

            manager.RaiseFrame("cam", 0, true);
            manager.RaiseFrame("cam", 1, false);
            manager.RaiseSegment("cam", "last.ts");

            var stop = host.StopAsync();
            Assert.False(manager.HasSubscribers);
            gate.SetResult();
            await stop;

            Assert.Equal(2, plugin.Frames.Count);
            Assert.Single(plugin.Segments);
            Assert.True(plugin.Stopped);
            Assert.True(plugin.Disposed);
            Assert.Equal(PluginState.Stopped, Status(host, "draining").State);
        }

        [Fact]
        public async Task HungPluginShouldNotBlockShutdown()
        {
            var manager = new FakeRecordingManager("cam");
            var hung = new TestPlugin("hung") { FrameGate = new TaskCompletionSource().Task, HangOnStop = true };

            var host = Builder(manager).SetStopTimeout(TimeSpan.FromMilliseconds(200)).AddPlugin(hung).Build();
            await host.StartAsync();
            manager.RaiseFrame("cam", 0, true);

            var watch = Stopwatch.StartNew();
            await host.StopAsync();

            Assert.True(watch.ElapsedMilliseconds < 5000, $"stop took {watch.ElapsedMilliseconds} ms");
            Assert.True(hung.Disposed);
        }

        [Fact]
        public void BuilderShouldValidate()
        {
            var manager = new FakeRecordingManager("cam");

            Assert.Throws<InvalidOperationException>(() => PluginHostBuilder.GetBuilder().Build());
            Assert.Throws<ArgumentException>(() => Builder(manager).AddPlugin(new TestPlugin("same")).AddPlugin(new TestPlugin("SAME")));
            Assert.Throws<ArgumentException>(() => Builder(manager).AddPlugin(new TestPlugin(" ")));
            Assert.Throws<ArgumentNullException>(() => Builder(manager).AddPlugin(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => Builder(manager).SetFrameQueueCapacity(0));
            Assert.Throws<ArgumentNullException>(() => new PluginEvent(""));
        }

        #endregion
    }
}
