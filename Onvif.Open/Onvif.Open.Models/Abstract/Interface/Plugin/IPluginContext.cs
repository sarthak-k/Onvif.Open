// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Plugin;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Services the host offers to a plugin.
    /// </summary>
    public interface IPluginContext
    {
        #region Public Variables

        string PluginName { get; }

        /// <summary>The plugin's own settings ("Settings" section of its configuration), keys like "Schedule:Start".</summary>
        IReadOnlyDictionary<string, string> Settings { get; }

        ILogger Logger { get; }

        TimeProvider TimeProvider { get; }

        /// <summary>The cameras assigned to this plugin.</summary>
        IPluginCameras Cameras { get; }

        #endregion

        #region Public Methods

        /// <summary>Records an event (visible in the API) and forwards it to <see cref="IPluginEventObserver"/> plugins.</summary>
        Task PublishAsync(PluginEvent pluginEvent, CancellationToken cancellationToken = default);

        #endregion
    }
}
