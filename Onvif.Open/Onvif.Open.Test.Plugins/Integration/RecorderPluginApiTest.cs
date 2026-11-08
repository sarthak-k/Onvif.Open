// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Test.Plugins.Fakes;
using Xunit;

namespace Onvif.Open.Test.Plugins.Integration
{
    public class RecorderPluginApiTest
    {
        #region Private Methods

        private static WebApplicationFactory<Program> Factory(string storage, string plugins, params (string Key, string Value)[] settings)
        {
            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Recording:StorageDirectory", storage);
                builder.UseSetting("Settings:Database", System.IO.Path.Combine(System.IO.Path.GetTempPath(), "onvif-open-settings", System.Guid.NewGuid().ToString("N"), "settings.db"));
                builder.UseSetting("Recording:Retention:MinFreeSpaceGB", "0");
                builder.UseSetting("Recording:Cameras:0:Id", "room-1");
                builder.UseSetting("Recording:Cameras:0:RtspUrl", "rtsp://127.0.0.1:1/stream");
                builder.UseSetting("Plugins:Directory", plugins);

                foreach (var (key, value) in settings)
                {
                    builder.UseSetting(key, value);
                }
            });
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task ConfiguredPluginShouldBeLoadedAndReported()
        {
            using (var folders = new PluginFolders())
            using (var factory = Factory(Path.Combine(folders.Root, "recordings"), folders.Root,
                ($"Plugins:Items:{PluginFolders.SampleFolder}:Cameras:0", "room-1"),
                ($"Plugins:Items:{PluginFolders.SampleFolder}:Settings:ReportEveryFrames", "10")))
            using (var client = factory.CreateClient())
            {
                var plugins = JsonDocument.Parse(await client.GetStringAsync("/api/plugins")).RootElement;

                var plugin = Assert.Single(plugins.EnumerateArray());
                Assert.Equal("Sample", plugin.GetProperty("name").GetString());
                Assert.Equal("Running", plugin.GetProperty("state").GetString());
                Assert.Equal("room-1", plugin.GetProperty("cameras")[0].GetString());

                var events = JsonDocument.Parse(await client.GetStringAsync("/api/events?cameraId=room-1&limit=10")).RootElement;
                Assert.Equal(0, events.GetArrayLength());

                // RTSP-only camera: no ONVIF snapshot.
                Assert.Equal(HttpStatusCode.NotImplemented, (await client.GetAsync("/api/cameras/room-1/snapshot")).StatusCode);
                Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/cameras/unknown/snapshot")).StatusCode);
            }
        }

        [Fact]
        public async Task UnlistedOrDisabledPluginsShouldNotBeLoaded()
        {
            using (var folders = new PluginFolders())
            using (var factory = Factory(Path.Combine(folders.Root, "recordings"), folders.Root,
                ($"Plugins:Items:{PluginFolders.SampleFolder}:Enabled", "false")))
            using (var client = factory.CreateClient())
            {
                var plugins = JsonDocument.Parse(await client.GetStringAsync("/api/plugins")).RootElement;
                Assert.Equal(0, plugins.GetArrayLength());
            }
        }

        [Fact]
        public void PluginForUnknownCameraShouldFailStartup()
        {
            using (var folders = new PluginFolders())
            using (var factory = Factory(Path.Combine(folders.Root, "recordings"), folders.Root,
                ($"Plugins:Items:{PluginFolders.SampleFolder}:Cameras:0", "nope")))
            {
                var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
                Assert.Contains("unknown camera", error.ToString());
            }
        }

        #endregion
    }
}
