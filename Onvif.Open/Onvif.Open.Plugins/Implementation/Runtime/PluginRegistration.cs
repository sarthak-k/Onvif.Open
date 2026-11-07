// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Abstract.Interface.Plugin;

namespace Onvif.Open.Plugins.Implementation.Runtime
{
    internal sealed class PluginRegistration
    {
        #region Constructor

        public PluginRegistration(IPlugin plugin, IEnumerable<string> cameras, IReadOnlyDictionary<string, string> settings)
        {
            Plugin = plugin;
            Cameras = cameras?.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Settings = settings ?? new Dictionary<string, string>();
        }

        #endregion

        #region Public Variables

        public IPlugin Plugin { get; }

        /// <summary>Null means every camera.</summary>
        public IReadOnlyList<string> Cameras { get; }

        public IReadOnlyDictionary<string, string> Settings { get; }

        #endregion
    }
}
