// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Plugins.Implementation.Builder;
using Onvif.Open.Plugins.Implementation.Loading;
using Onvif.Open.Test.Plugins.Fakes;
using Xunit;

namespace Onvif.Open.Test.Plugins.Integration
{
    public class PluginLoaderTest
    {
        #region Public Methods

        [Fact]
        public void ShouldLoadPluginIntoItsOwnContextSharingTheContracts()
        {
            using (var folders = new PluginFolders())
            {
                var plugin = Assert.Single(PluginLoader.LoadFromFolder(folders.SamplePath));

                Assert.Equal("Sample", plugin.Name);
                Assert.IsAssignableFrom<IFrameProcessor>(plugin);
                Assert.IsAssignableFrom<ICameraStateObserver>(plugin);

                var context = AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly);
                Assert.NotSame(AssemblyLoadContext.Default, context);
                Assert.StartsWith("Plugin:", context.Name);

                // The SDK contract comes from the host, not from a private copy.
                Assert.Same(typeof(IPlugin).Assembly, plugin.GetType().GetInterface(nameof(IPlugin)).Assembly);
                Assert.False(File.Exists(Path.Combine(folders.SamplePath, "Onvif.Open.Core.dll")));
            }
        }

        [Fact]
        public async Task LoadedPluginShouldRunInHost()
        {
            using (var folders = new PluginFolders())
            {
                var manager = new FakeRecordingManager("cam");

                await using (var host = PluginHostBuilder.GetBuilder()
                    .SetRecordingManager(manager)
                    .AddPlugins(PluginLoader.LoadFromFolder(folders.SamplePath), settings: new Dictionary<string, string> { ["ReportEveryFrames"] = "2" })
                    .Build())
                {
                    await host.StartAsync();

                    for (var i = 0; i < 10; i++)
                    {
                        manager.RaiseFrame("cam", i, i % 2 == 0);
                    }

                    await Eventually.True(() => host.GetEvents().Count >= 2, "the sample plugin published events");

                    var status = Assert.Single(host.GetStatus());
                    Assert.Equal(PluginState.Running, status.State);
                    Assert.Equal(5, status.FramesDelivered);
                    Assert.All(host.GetEvents(), e => Assert.Equal("sample.frames", e.Type));
                    Assert.Equal("4", host.GetEvents().First().Data["keyFrames"]);
                }
            }
        }

        [Fact]
        public void MissingOrEmptyFoldersShouldFailClearly()
        {
            using (var folders = new PluginFolders())
            {
                Assert.Throws<FileNotFoundException>(() => PluginLoader.LoadFromFolder(Path.Combine(folders.Root, "Missing")));

                var noPlugins = Path.Combine(folders.Root, "NoPlugins");
                Directory.CreateDirectory(noPlugins);
                File.Copy(typeof(PluginLoaderTest).Assembly.Location.Replace("Onvif.Open.Test.Plugins.dll", "xunit.core.dll"), Path.Combine(noPlugins, "NoPlugins.dll"));
                Assert.Throws<InvalidOperationException>(() => PluginLoader.LoadFromFolder(noPlugins));
            }
        }

        #endregion
    }
}
