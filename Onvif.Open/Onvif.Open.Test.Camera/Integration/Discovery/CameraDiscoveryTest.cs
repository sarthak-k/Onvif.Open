// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Discovery;
using Onvif.Open.Discovery.Implementation.Builder;
using Onvif.Open.Test.Camera.Fakes;
using Xunit;

namespace Onvif.Open.Test.Camera.Integration.Discovery
{
    public class CameraDiscoveryTest
    {
        #region Private Methods

        private static CameraDiscoveryBuilder Builder(params FakeDiscoveryDevice[] devices)
        {
            var builder = CameraDiscoveryBuilder.GetBuilder().SetMulticast(false).SetTimeout(TimeSpan.FromMilliseconds(800));

            foreach (var device in devices)
            {
                builder.AddTarget(IPAddress.Loopback, device.Port);
            }

            return builder;
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task ShouldFindDevicesAndDeduplicateRepeatedAnswers()
        {
            using (var first = new FakeDiscoveryDevice("11111111-1111-1111-1111-111111111111", "Lobby", "http://192.168.1.10/onvif/device_service"))
            using (var second = new FakeDiscoveryDevice("22222222-2222-2222-2222-222222222222", "Parking Lot", "http://192.168.1.11:8000/onvif/device_service"))
            {
                var devices = await Builder(first, second).Build().FindAsync();

                Assert.Equal(new[] { "Lobby", "Parking Lot" }, devices.Select(d => d.Name).OrderBy(n => n));
                Assert.All(devices, d => Assert.Equal(IPAddress.Loopback, d.RespondedFrom));

                // Two device types, each sent twice: four probes per device, one result per device.
                Assert.Equal(4, first.Probes.Count);
                Assert.Contains(first.Probes, p => p.Contains("dn:NetworkVideoTransmitter"));
                Assert.Contains(first.Probes, p => p.Contains("tds:Device"));
            }
        }

        [Fact]
        public async Task DiscoverAsyncShouldYieldBeforeTheTimeout()
        {
            using (var device = new FakeDiscoveryDevice("33333333-3333-3333-3333-333333333333", "Fast", "http://192.168.1.12/onvif/device_service"))
            {
                var discovery = Builder(device).SetTimeout(TimeSpan.FromSeconds(5)).Build();
                var watch = Stopwatch.StartNew();

                await foreach (var found in discovery.DiscoverAsync())
                {
                    Assert.Equal("Fast", found.Name);
                    break;
                }

                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"took {watch.Elapsed}");
            }
        }

        [Fact]
        public async Task AnswersToOtherProbesShouldBeIgnoredButMissingRelatesToAccepted()
        {
            using (var foreign = new FakeDiscoveryDevice("44444444-4444-4444-4444-444444444444", "Foreign", "http://192.168.1.13/onvif/device_service") { WrongRelatesTo = true })
            using (var sloppy = new FakeDiscoveryDevice("55555555-5555-5555-5555-555555555555", "Sloppy", "http://192.168.1.14/onvif/device_service") { OmitRelatesTo = true })
            {
                var devices = await Builder(foreign, sloppy).Build().FindAsync();

                Assert.Equal("Sloppy", Assert.Single(devices).Name);
            }
        }

        [Fact]
        public async Task LateAnswersShouldBeMissedWithoutError()
        {
            using (var slow = new FakeDiscoveryDevice("66666666-6666-6666-6666-666666666666", "Slow", "http://192.168.1.15/onvif/device_service") { Delay = TimeSpan.FromSeconds(2) })
            {
                var watch = Stopwatch.StartNew();
                var devices = await Builder(slow).SetTimeout(TimeSpan.FromMilliseconds(300)).Build().FindAsync();

                Assert.Empty(devices);
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
            }
        }

        [Fact]
        public async Task CancellationShouldStopDiscovery()
        {
            using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    CameraDiscoveryBuilder.GetBuilder().SetTimeout(TimeSpan.FromSeconds(30)).Build().FindAsync(cancel.Token));
            }
        }

        [Fact]
        public async Task MulticastDiscoveryShouldCompleteOnAnyNetwork()
        {
            // No camera on the test network: the probe must still be sent and end cleanly after the timeout.
            var devices = await CameraDiscoveryBuilder.GetBuilder().SetTimeout(TimeSpan.FromMilliseconds(300)).Build().FindAsync();

            Assert.NotNull(devices);
        }

        [Fact]
        public void BuilderShouldValidate()
        {
            Assert.Throws<InvalidOperationException>(() => CameraDiscoveryBuilder.GetBuilder().SetMulticast(false).Build());
            Assert.Throws<ArgumentOutOfRangeException>(() => CameraDiscoveryBuilder.GetBuilder().SetTimeout(TimeSpan.Zero));
            Assert.Throws<ArgumentNullException>(() => CameraDiscoveryBuilder.GetBuilder().AddTarget(null));
        }

        [Fact]
        public void DiscoveredDeviceServiceUrlShouldBeKeptByCameraBuilder()
        {
            var device = new DiscoveredDevice("urn:uuid:1", new[] { new Uri("http://10.0.0.20:8080/onvif/services") }, null, null, null);

            var camera = CameraBuilder.GetBuilder().SetDeviceServiceUrl(device.DeviceServiceUri.ToString()).Build();

            Assert.Equal("http://10.0.0.20:8080/onvif/services", camera.GetCameraInformation().CameraUri.ToString());
            Assert.Equal("http://10.0.0.20/onvif/device_service", CameraBuilder.GetBuilder().SetDeviceServiceUrl("http://10.0.0.20").Build().GetCameraInformation().CameraUri.ToString());
        }

        #endregion
    }
}
