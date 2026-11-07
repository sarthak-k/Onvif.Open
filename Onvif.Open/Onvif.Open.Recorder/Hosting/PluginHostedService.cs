// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Onvif.Open.Core.Abstract.Interface.Plugin;

namespace Onvif.Open.Recorder.Hosting
{
    /// <summary>
    /// Registered before the recording service: plugins start before recording (so they see the first
    /// state changes) and stop after it (so they receive the last segments).
    /// </summary>
    internal sealed class PluginHostedService : IHostedService
    {
        #region Constructor

        public PluginHostedService(IPluginHost host)
        {
            _host = host;
        }

        #endregion

        #region Private Variables

        private readonly IPluginHost _host;

        #endregion

        #region Public Methods

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _host.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _host.StopAsync(cancellationToken);
        }

        #endregion
    }
}
