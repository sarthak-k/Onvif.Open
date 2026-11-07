// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Onvif.Open.Core.Abstract.Interface.Recording;

namespace Onvif.Open.Recorder.Hosting
{
    /// <summary>
    /// Starts recording with the host and stops it (closing the open segments) on shutdown.
    /// </summary>
    internal sealed class RecordingHostedService : IHostedService
    {
        #region Constructor

        public RecordingHostedService(IRecordingManager manager)
        {
            _manager = manager;
        }

        #endregion

        #region Private Variables

        private readonly IRecordingManager _manager;

        #endregion

        #region Public Methods

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return _manager.StartAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return _manager.StopAsync(cancellationToken);
        }

        #endregion
    }
}
