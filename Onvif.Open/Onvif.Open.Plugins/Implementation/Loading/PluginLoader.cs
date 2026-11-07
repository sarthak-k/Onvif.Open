// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.Storage;

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
            var assembly = LoadAssembly(folder, out var mainAssembly);
            var plugins = CreateInstances<IPlugin>(assembly);

            if (plugins.Count == 0)
            {
                throw new InvalidOperationException($"'{mainAssembly}' does not contain a public {nameof(IPlugin)} implementation with a parameterless constructor.");
            }

            return plugins;
        }

        /// <summary>
        /// Loads a storage plugin: <c>{folder}/{FolderName}.dll</c> with exactly one public
        /// <see cref="ISegmentStoreProvider"/> that has a parameterless constructor.
        /// </summary>
        public static ISegmentStoreProvider LoadStorageProvider(string folder)
        {
            var assembly = LoadAssembly(folder, out var mainAssembly);
            var providers = CreateInstances<ISegmentStoreProvider>(assembly);

            if (providers.Count != 1)
            {
                throw new InvalidOperationException($"'{mainAssembly}' must contain exactly one public {nameof(ISegmentStoreProvider)} implementation with a parameterless constructor (found {providers.Count}).");
            }

            return providers[0];
        }

        #endregion

        #region Private Methods

        private static Assembly LoadAssembly(string folder, out string mainAssembly)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new ArgumentNullException(nameof(folder));
            }

            var fullPath = Path.GetFullPath(folder);
            mainAssembly = Path.Combine(fullPath, Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar)) + ".dll");

            if (!File.Exists(mainAssembly))
            {
                throw new FileNotFoundException($"Plugin assembly '{mainAssembly}' was not found. A plugin folder must contain an assembly named after the folder.", mainAssembly);
            }

            var context = new PluginLoadContext(mainAssembly);
            return context.LoadFromAssemblyPath(mainAssembly);
        }

        private static IReadOnlyList<T> CreateInstances<T>(Assembly assembly)
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
                .Where(t => t.IsClass && !t.IsAbstract && typeof(T).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .Select(t => (T)Activator.CreateInstance(t))
                .ToList();
        }

        #endregion
    }
}
