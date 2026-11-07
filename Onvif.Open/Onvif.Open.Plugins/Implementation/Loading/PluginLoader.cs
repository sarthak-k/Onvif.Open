// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Onvif.Open.Core.Abstract.Interface.Plugin;

namespace Onvif.Open.Plugins.Implementation.Loading
{
    /// <summary>
    /// Loads plugins built as separate assemblies. A plugin folder contains the build output of one
    /// plugin project: <c>{folder}/{FolderName}.dll</c> plus its dependencies and <c>.deps.json</c>.
    /// </summary>
    public static class PluginLoader
    {
        #region Public Methods

        /// <summary>
        /// Loads <c>{folder}/{FolderName}.dll</c> into its own load context and creates every public,
        /// non-abstract <see cref="IPlugin"/> type that has a parameterless constructor.
        /// </summary>
        public static IReadOnlyList<IPlugin> LoadFromFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentNullException(nameof(folder));
            }

            var fullPath = Path.GetFullPath(folder);
            var mainAssembly = Path.Combine(fullPath, Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)) + ".dll");

            if (!File.Exists(mainAssembly))
            {
                throw new FileNotFoundException($"Plugin assembly '{mainAssembly}' was not found. A plugin folder must contain an assembly named after the folder.", mainAssembly);
            }

            var context = new PluginLoadContext(mainAssembly);
            var assembly = context.LoadFromAssemblyPath(mainAssembly);
            var plugins = CreatePlugins(assembly);

            if (plugins.Count == 0)
            {
                throw new InvalidOperationException($"'{mainAssembly}' does not contain a public {nameof(IPlugin)} implementation with a parameterless constructor.");
            }

            return plugins;
        }

        #endregion

        #region Private Methods

        private static IReadOnlyList<IPlugin> CreatePlugins(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                throw new InvalidOperationException($"Could not load the types of plugin '{assembly.GetName().Name}': {ex.LoaderExceptions.FirstOrDefault()?.Message}", ex);
            }

            return types
                .Where(t => t.IsClass && !t.IsAbstract && typeof(IPlugin).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (IPlugin)Activator.CreateInstance(t))
                .ToList();
        }

        #endregion
    }
}
