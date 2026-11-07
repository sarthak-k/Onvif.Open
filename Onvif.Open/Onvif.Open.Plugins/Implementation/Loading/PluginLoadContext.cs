// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Onvif.Open.Plugins.Implementation.Loading
{
    /// <summary>
    /// Load context of one plugin folder. Assemblies the host already provides (Onvif.Open.Core,
    /// Microsoft.Extensions.*, ...) are shared so the plugin contracts have one identity; everything
    /// else (the plugin's own dependencies, native libraries) is loaded from the plugin folder.
    /// </summary>
    internal sealed class PluginLoadContext : AssemblyLoadContext
    {
        #region Constructor

        public PluginLoadContext(string mainAssemblyPath)
            : base($"Plugin:{Path.GetFileNameWithoutExtension(mainAssemblyPath)}", isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        #endregion

        #region Private Variables

        private readonly AssemblyDependencyResolver _resolver;

        #endregion

        #region Protected Methods

        protected override Assembly Load(AssemblyName assemblyName)
        {
            // Prefer the host's copy: plugins must see the same IPlugin/VideoFrame/... types as the host.
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
            }
            catch (FileLoadException)
            {
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path == null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }

        #endregion
    }
}
