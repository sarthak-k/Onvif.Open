// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Events;
using Onvif.Open.Device.Management.Implementation.Builder;
using Onvif.Open.Events.Implementation.Builder;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Events
{
    public class EventServiceTest
    {
        #region Private Methods

        private static FakeOnvifServer Device()
        {
            return new FakeOnvifServer(new Uri("rtsp://192.168.1.3/stream"));
        }

        private static CameraEventsOptions FastOptions(ConcurrentQueue<Exception> errors = null)
        {
            return new CameraEventsOptions
            {
                CameraId = "cam1",
                PullTimeout = TimeSpan.FromSeconds(1),
                MinRetryDelay = TimeSpan.FromMilliseconds(20),
                MaxRetryDelay = TimeSpan.FromMilliseconds(100),
                OnError = e => errors?.Enqueue(e)
            };
        }

        private static async Task<List<CameraEvent>> ReadAsync(IAsyncEnumerable<CameraEvent> events, int count)
        {
            var received = new List<CameraEvent>();

            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await foreach (var cameraEvent in events.WithCancellation(timeout.Token))
                {
                    received.Add(cameraEvent);
                    if (received.Count == count)
                    {
                        break;
                    }
                }
            }

            return received;
        }

        #endregion

        #region Public Methods

        [Fact]
        public void DeviceManagementShouldResolveEventServiceFromCapabilities()
        {
            using (var device = Device())
            {
                var deviceManagement = DeviceManagementBuilder.GetBuilder().SetCamera(new TestCameraInformation(device.DeviceServiceUri)).Build();

                Assert.Equal(device.EventServiceUri, deviceManagement.GetEventServiceUri());
                Assert.Equal(device.MediaServiceUri, deviceManagement.GetMediaServiceUri());
            }
        }

        [Fact]
        public async Task SubscriptionShouldPullRenewAndUnsubscribeWithAddressingHeaders()
        {
            using (var device = Device())
            {
                var service = EventServiceBuilder.GetBuilder().SetCamera(new TestCameraInformation(device.DeviceServiceUri)).SetServiceUri(device.EventServiceUri).Build();

                var subscription = await service.CreatePullPointSubscriptionAsync(TimeSpan.FromSeconds(60));
                Assert.Equal($"/onvif/subscription/1", subscription.Address.AbsolutePath);

                device.PendingNotifications.Enqueue(FakeOnvifServer.MotionNotification(true));
                device.PendingNotifications.Enqueue(FakeOnvifServer.MotionNotification(false));

                var events = await subscription.PullMessagesAsync(TimeSpan.FromSeconds(1), 10, "cam1");
                Assert.Equal(new bool?[] { true, false }, events.Select(e => e.GetState()));
                Assert.All(events, e => Assert.Equal("cam1", e.CameraId));

                Assert.Empty(await subscription.PullMessagesAsync(TimeSpan.FromSeconds(1), 10));

                await subscription.RenewAsync(TimeSpan.FromSeconds(60));
                await subscription.DisposeAsync();
                await subscription.DisposeAsync();

                Assert.Equal(1, device.Renewals);
                Assert.Equal(1, device.Unsubscribes);

                var create = device.RequestBodies.First(b => b.Contains("CreatePullPointSubscription"));
                Assert.Contains("PT1M", create);
                Assert.Contains("UsernameToken", create);

                // Every request to the subscription carries wsa:To, the action and the echoed reference parameter.
                var pull = device.SubscriptionRequests.First(b => b.Contains("PullMessages"));
                Assert.Contains($"http://127.0.0.1:{device.Port}/onvif/subscription/1</a:To>", pull);
                Assert.Contains("http://www.onvif.org/ver10/events/wsdl/PullPointSubscription/PullMessagesRequest", pull);
                Assert.Contains("sub-1</", pull);
                Assert.Contains("SubscriptionId", pull);
                Assert.Contains("<Timeout>PT1S</Timeout>", pull.Replace("tev:", string.Empty).Replace(" xmlns=\"http://www.onvif.org/ver10/events/wsdl\"", string.Empty));
                Assert.All(device.SubscriptionRequests, b => Assert.Contains("sub-1</", b));
            }
        }

        [Fact]
        public async Task CameraEventsShouldStreamEventsAndUnsubscribeWhenStopped()
        {
            using (var device = Device())
            {
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var events = CameraEvents.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build(), options: FastOptions());

                device.PendingNotifications.Enqueue(FakeOnvifServer.MotionNotification(false, "Initialized"));
                device.PendingNotifications.Enqueue(FakeOnvifServer.MotionNotification(true));

                var received = await ReadAsync(events.ReadEventsAsync(), 2);

                Assert.True(received[0].IsInitialState);
                Assert.True(received[1].GetState());
                Assert.All(received, e => Assert.Equal("cam1", e.CameraId));
                await Eventually(() => device.Unsubscribes == 1, "the subscription was removed");
            }
        }

        [Fact]
        public async Task CameraEventsShouldResubscribeWhenTheSubscriptionExpires()
        {
            using (var device = Device())
            {
                var errors = new ConcurrentQueue<Exception>();
                var camera = new TestCameraInformation(device.DeviceServiceUri);
                var events = CameraEvents.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build(), options: FastOptions(errors));

                device.PendingNotifications.Enqueue(FakeOnvifServer.MotionNotification(true));

                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var received = 0;
                    await foreach (var cameraEvent in events.ReadEventsAsync(timeout.Token))
                    {
                        received++;
                        if (received == 1)
                        {
                            // The device drops the subscription (reboot, timeout, ...); the next event arrives on a new one.
                            device.ExpireSubscription = true;
                            device.PendingNotifications.Enqueue(FakeOnvifServer.MotionNotification(false));
                        }
                        else
                        {
                            Assert.False(cameraEvent.GetState());
                            break;
                        }
                    }
                }

                Assert.Equal(2, device.SubscriptionsCreated);
                Assert.Single(errors);
            }
        }

        [Fact]
        public async Task UnreachableEventServiceShouldRetryUntilCancelled()
        {
            var errors = new ConcurrentQueue<Exception>();
            var camera = new TestCameraInformation(new Uri("http://127.0.0.1:1/onvif/device_service"));
            var events = CameraEvents.CreateInstance(camera, DeviceManagementBuilder.GetBuilder().SetCamera(camera).Build(), options: FastOptions(errors));

            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
            {
                await foreach (var unused in events.ReadEventsAsync(timeout.Token))
                {
                    Assert.Fail("no events expected");
                }
            }

            Assert.True(errors.Count >= 2, $"{errors.Count} attempts");
        }

        #endregion

        #region Private Methods

        private static async Task Eventually(Func<bool> condition, string because)
        {
            var stop = DateTime.UtcNow.AddSeconds(10);
            while (!condition())
            {
                if (DateTime.UtcNow > stop)
                {
                    Assert.Fail("Timed out waiting until " + because);
                }

                await Task.Delay(10);
            }
        }

        #endregion
    }
}
