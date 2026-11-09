// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Test.Plugins.Fakes;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Plugins.Integration
{
    public class RecorderPluginApiTest
    {
        #region Private Methods

        private static WebApplicationFactory<Program> Factory(TestSettings settings, string storage, string plugins, StoredPlugin plugin = null)
        {
            settings.Camera(new CameraOptions { Id = "room-1", RtspUrl = "rtsp://127.0.0.1:1/stream" });

            if (plugin != null)
            {
                settings.Plugin(plugin);
            }

            return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                settings.Apply(builder);
                builder.UseSetting("Recording:StorageDirectory", storage);
                builder.UseSetting("Plugins:Directory", plugins);
            });
        }

        #endregion

        #region Public Methods

        [Fact]
        public async Task ConfiguredPluginShouldBeLoadedAndReported()
        {
            using (var folders = new PluginFolders())
            using (var settings = new TestSettings())
            using (var factory = Factory(settings, Path.Combine(folders.Root, "recordings"), folders.Root,
                new StoredPlugin { Folder = PluginFolders.SampleFolder, Cameras = { "room-1" }, Settings = { ["ReportEveryFrames"] = "10" } }))
            using (var client = TestSettings.Authenticate(factory.CreateClient()))
            {
                var plugins = JsonDocument.Parse(await client.GetStringAsync("/api/plugins")).RootElement;

                var plugin = Assert.Single(plugins.EnumerateArray());
                Assert.Equal("Sample", plugin.GetProperty("name").GetString());
                Assert.Equal("Running", plugin.GetProperty("state").GetString());
                Assert.Equal("room-1", plugin.GetProperty("cameras")[0].GetString());

                var events = JsonDocument.Parse(await client.GetStringAsync("/api/events?cameraId=room-1&category=plugin&limit=10")).RootElement;
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
            using (var settings = new TestSettings())
            using (var factory = Factory(settings, Path.Combine(folders.Root, "recordings"), folders.Root,
                new StoredPlugin { Folder = PluginFolders.SampleFolder, Enabled = false }))
            using (var client = TestSettings.Authenticate(factory.CreateClient()))
            {
                var plugins = JsonDocument.Parse(await client.GetStringAsync("/api/plugins")).RootElement;
                Assert.Equal(0, plugins.GetArrayLength());
            }
        }

        [Fact]
        public void PluginForUnknownCameraShouldFailStartup()
        {
            using (var folders = new PluginFolders())
            using (var settings = new TestSettings())
            using (var factory = Factory(settings, Path.Combine(folders.Root, "recordings"), folders.Root,
                new StoredPlugin { Folder = PluginFolders.SampleFolder, Cameras = { "nope" } }))
            {
                var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
                Assert.Contains("unknown camera", error.ToString());
            }
        }

        #endregion
    }
}
