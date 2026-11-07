// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;

namespace Onvif.Open.Test.Plugins.Fakes
{
    /// <summary>
    /// Creates a plugin directory holding a copy of the sample plugin's build output, like a deployment.
    /// </summary>
    internal sealed class PluginFolders : IDisposable
    {
        public const string SampleFolder = "Onvif.Open.Plugins.Sample";

        public PluginFolders()
        {
            Root = Path.Combine(Path.GetTempPath(), "onvif-open-plugin-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);

            var testOutput = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            var framework = testOutput.Name;
            var configuration = testOutput.Parent.Name;
            var projects = testOutput.Parent.Parent.Parent.Parent.FullName;
            var sampleOutput = Path.Combine(projects, SampleFolder, "bin", configuration, framework);

            SamplePath = Path.Combine(Root, SampleFolder);
            Directory.CreateDirectory(SamplePath);

            foreach (var file in Directory.GetFiles(sampleOutput))
            {
                File.Copy(file, Path.Combine(SamplePath, Path.GetFileName(file)));
            }
        }

        public string Root { get; }
        public string SamplePath { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
                // Assemblies loaded into a non-collectible context stay locked on Windows.
            }
        }
    }
}
