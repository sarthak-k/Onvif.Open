// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Plugin;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Receives the events published by other plugins, e.g. to send notifications.
    /// A plugin never receives its own events.
    /// </summary>
    public interface IPluginEventObserver : IPlugin
    {
        #region Public Methods

        Task OnPluginEventAsync(PluginEvent pluginEvent, CancellationToken cancellationToken);

        #endregion
    }
}
