// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Notified after a segment file has been closed, e.g. to upload or index it.
    /// The file may be deleted later by the retention policy.
    /// </summary>
    public interface ISegmentObserver : IPlugin
    {
        #region Public Methods

        Task OnSegmentCompletedAsync(RecordingSegment segment, CancellationToken cancellationToken);

        #endregion
    }
}
