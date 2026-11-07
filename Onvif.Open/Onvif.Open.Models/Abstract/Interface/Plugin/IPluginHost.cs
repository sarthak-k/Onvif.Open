// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Plugin;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Runs plugins next to a recording manager and routes frames, state changes, segments and events to them.
    /// </summary>
    public interface IPluginHost : IAsyncDisposable
    {
        #region Public Variables

        /// <summary>Raised for every event a plugin publishes.</summary>
        event EventHandler<PluginEvent> EventPublished;

        #endregion

        #region Public Methods

        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>Delivers what is still queued (within the stop timeout) and stops every plugin.</summary>
        Task StopAsync(CancellationToken cancellationToken = default);

        IReadOnlyList<PluginStatus> GetStatus();

        /// <summary>Recent events, newest first.</summary>
        IReadOnlyList<PluginEvent> GetEvents(PluginEventQuery query = null);

        #endregion
    }
}
