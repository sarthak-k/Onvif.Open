// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.Core.DeviceIo;
using Onvif.Open.Core.Events;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.DeviceIo
{
    public class CameraIoTest
    {
        #region Private Methods

        private static FakeOnvifServer Device()
        {
            return new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream"));
        }

        private static ICameraIo Io(FakeOnvifServer device)
        {
            var camera = new TestCameraInformation(device.DeviceServiceUri);
            var deviceManagement = DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build();
            var events = CameraEvents.CreateInstance(camera, deviceManagement, options: new CameraEventsOptions { PullTimeout = TimeSpan.FromSeconds(1) });
            return CameraIo.CreateInstance(camera, deviceManagement, events);
        }

        private static string InputEvent(string token, string state, string operation = "Changed")
        {
            return "<wsnt:NotificationMessage xmlns:wsnt=\"http://docs.oasis-open.org/wsn/b-2\" xmlns:tns1=\"http://www.onvif.org/ver10/topics\">" +
                   "<wsnt:Topic Dialect=\"http://www.onvif.org/ver10/tev/topicExpression/ConcreteSet\">tns1:Device/Trigger/DigitalInput</wsnt:Topic>" +
                   $"<wsnt:Message><tt:Message UtcTime=\"2026-11-07T10:00:00Z\" PropertyOperation=\"{operation}\">" +
                   $"<tt:Source><tt:SimpleItem Name=\"InputToken\" Value=\"{token}\"/></tt:Source>" +
                   $"<tt:Data><tt:SimpleItem Name=\"LogicalState\" Value=\"{state}\"/></tt:Data>" +
                   "</tt:Message></wsnt:Message></wsnt:NotificationMessage>";
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task RelaysShouldBeListedAndSwitched()
        {
            using (var device = Device())
            {
                var io = Io(device);

                var relays = await io.GetRelayOutputsAsync();
                Assert.Equal(new[] { "Relay_1", "Relay_2" }, relays.Select(r => r.Token));
                Assert.Equal(RelayMode.Bistable, relays[0].Mode);
                Assert.Equal(IdleState.Open, relays[0].IdleState);
                Assert.Equal(RelayMode.Monostable, relays[1].Mode);
                Assert.Equal(TimeSpan.FromSeconds(5), relays[1].DelayTime);

                await io.SetRelayAsync("Relay_1", true);
                Assert.True(device.Relays["Relay_1"].Active);
                await io.SetRelayAsync("Relay_1", false);
                Assert.False(device.Relays["Relay_1"].Active);
            }
        }

        [Fact]
        public async Task PulseShouldDeactivateEvenWhenCancelled()
        {
            using (var device = Device())
            using (var cancel = new CancellationTokenSource())
            {
                var io = Io(device);

                await io.PulseRelayAsync("Relay_1", TimeSpan.FromMilliseconds(100));
                Assert.Equal(new[] { "Relay_1:active", "Relay_1:inactive" }, device.RelayOperations.ToArray());

                var pulse = io.PulseRelayAsync("Relay_2", TimeSpan.FromSeconds(30), cancel.Token);
                while (!device.Relays["Relay_2"].Active)
                {
                    await Task.Delay(10);
                }

                cancel.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pulse);
                Assert.False(device.Relays["Relay_2"].Active);
            }
        }

        [Fact]
        public async Task RelaySettingsShouldBeConfigured()
        {
            using (var device = Device())
            {
                await Io(device).ConfigureRelayAsync("Relay_1", RelayMode.Monostable, TimeSpan.FromSeconds(2), IdleState.Closed);

                Assert.Equal(("Monostable", "PT2S", "closed"), (device.Relays["Relay_1"].Mode, device.Relays["Relay_1"].Delay, device.Relays["Relay_1"].Idle));
            }
        }

        [Fact]
        public async Task DigitalInputsShouldBeListedThroughDeviceIo()
        {
            using (var device = Device())
            {
                var inputs = await Io(device).GetDigitalInputsAsync();

                Assert.Equal(new[] { "Input_1", "Input_2" }, inputs.Select(i => i.Token));
                Assert.Equal(IdleState.Closed, inputs[0].IdleState);
                Assert.Equal(IdleState.Open, inputs[1].IdleState);
            }
        }

        [Fact]
        public async Task DeviceWithoutDeviceIoShouldBeNotSupported()
        {
            using (var device = Device())
            {
                device.HasDeviceIo = false;
                await Assert.ThrowsAsync<NotSupportedException>(() => Io(device).GetDigitalInputsAsync());
            }
        }

        [Fact]
        public async Task DigitalInputChangesShouldComeFromEvents()
        {
            using (var device = Device())
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                device.PendingNotifications.Enqueue(InputEvent("Input_1", "false", "Initialized"));
                device.PendingNotifications.Enqueue(FakeOnvifServer.MotionNotification(true));
                device.PendingNotifications.Enqueue(InputEvent("Input_1", "true"));

                var changes = new List<IoStateChange>();
                await foreach (var change in Io(device).WatchDigitalInputsAsync(timeout.Token))
                {
                    changes.Add(change);
                    if (changes.Count == 2)
                    {
                        break;
                    }
                }

                Assert.Equal(new[] { false, true }, changes.Select(c => c.IsActive));
                Assert.True(changes[0].IsInitialState);
                Assert.All(changes, c => Assert.Equal("Input_1", c.Token));
            }
        }

        [Fact]
        public void RelayEventsWithActiveInactiveStatesShouldMap()
        {
            var relayEvent = new CameraEvent("cam", "tns1:Device/Trigger/Relay", DateTimeOffset.UtcNow, "Changed",
                new Dictionary<string, string> { ["RelayToken"] = "Relay_1" }, null,
                new Dictionary<string, string> { ["LogicalState"] = "inactive" });

            Assert.False(relayEvent.GetState());
            Assert.Null(CameraIo.ToDigitalInputChange(relayEvent));

            var input = new CameraEvent("cam", "tns1:Device/Trigger/DigitalInput", DateTimeOffset.UtcNow, null,
                new Dictionary<string, string> { ["InputToken"] = "In" }, null,
                new Dictionary<string, string> { ["LogicalState"] = "active" });
            Assert.True(CameraIo.ToDigitalInputChange(input).IsActive);
        }

        #endregion
    }
}
