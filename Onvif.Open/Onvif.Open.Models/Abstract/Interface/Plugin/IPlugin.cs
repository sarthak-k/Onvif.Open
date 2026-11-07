// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Base contract of every plugin. Implement one or more capability interfaces as well:
    /// <see cref="IFrameProcessor"/>, <see cref="ICameraStateObserver"/>, <see cref="ISegmentObserver"/>
    /// and <see cref="IPluginEventObserver"/>.
    /// </summary>
    /// <remarks>
    /// A plugin class needs a public parameterless constructor to be loaded from a plugin folder.
    /// Every callback of one plugin is invoked sequentially (never concurrently) per capability, on
    /// threads owned by the plugin host; an exception is logged and counted but does not stop the plugin.
    /// </remarks>
    public interface IPlugin
    {
        #region Public Variables

        /// <summary>Unique name, used in logs, events and the API.</summary>
        string Name { get; }

        #endregion

        #region Public Methods

        /// <summary>Called once before any notification. Throwing marks the plugin as faulted.</summary>
        Task StartAsync(IPluginContext context, CancellationToken cancellationToken);

        /// <summary>Called once on shutdown, after the pending notifications were delivered.</summary>
        Task StopAsync(CancellationToken cancellationToken);

        #endregion
    }
}
