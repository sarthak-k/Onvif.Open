// Sarthak Killedar
// 2026-11-11

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    /// <summary>Starting and stopping hosted gateways with the configured commands.</summary>
    public sealed class CloudGatewayHostTest
    {
        private static CommandCloudGatewayHost Host(string start, string stop = "true", TimeSpan? timeout = null) =>
            new CommandCloudGatewayHost(new CloudGatewayOptions
            {
                Start = new HostCommand { Command = "/bin/sh", Arguments = new List<string> { "-c", start } },
                Stop = new HostCommand { Command = "/bin/sh", Arguments = new List<string> { "-c", stop } },
                CommandTimeout = timeout ?? TimeSpan.FromSeconds(30)
            }, NullLogger<CommandCloudGatewayHost>.Instance);

        [Fact]
        public async Task TheGatewayShouldGetItsIdentityInTheEnvironmentNotTheArguments()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            using (var folder = new TempDirectory())
            {
                var file = Path.Combine(folder.Path, "{gatewayId}.txt");
                var id = Guid.NewGuid().ToString("D");
                var host = Host($"printf '%s|%s|%s|%s' \"$ControlPlane__Url\" \"$ControlPlane__GatewayId\" \"$ControlPlane__Token\" \"$Gateway__Location\" > '{file}'", $"rm '{file}'");

                await host.StartAsync(new CloudGatewayLaunch(id, "the-token", "https://us.cameras.example.com"));
                var started = Path.Combine(folder.Path, id + ".txt");
                Assert.Equal($"https://us.cameras.example.com|{id}|the-token|Cloud", File.ReadAllText(started));

                await host.StopAsync(id);
                Assert.False(File.Exists(started));
            }
        }

        [Fact]
        public async Task FailuresShouldSayWhatWentWrong()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var id = Guid.NewGuid().ToString("D");
            var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => Host("echo 'no space left for containers' >&2; exit 3").StartAsync(new CloudGatewayLaunch(id, "t", "https://x.example.com")));
            Assert.Equal("/bin/sh exited with 3: no space left for containers", failed.Message);

            await Assert.ThrowsAsync<TimeoutException>(() => Host("sleep 5", timeout: TimeSpan.FromMilliseconds(300)).StartAsync(new CloudGatewayLaunch(id, "t", "https://x.example.com")));

            // Only gateway ids go into the arguments.
            await Assert.ThrowsAsync<ArgumentException>(() => Host("true").StopAsync("x; rm -rf /"));
        }
    }
}
