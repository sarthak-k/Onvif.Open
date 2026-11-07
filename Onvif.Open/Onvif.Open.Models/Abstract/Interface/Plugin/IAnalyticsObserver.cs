// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Analytics;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Receives what the cameras' own analytics detect (ONVIF Profile M metadata): objects with class, confidence and
    /// position, several times per second while they are in view. Only for cameras with analytics enabled
    /// ("Analytics": { "Enabled": true }). When a plugin falls behind, the oldest frames are dropped.
    /// </summary>
    public interface IAnalyticsObserver : IPlugin
    {
        #region Public Methods

        Task OnAnalyticsAsync(AnalyticsFrame frame, CancellationToken cancellationToken);

        #endregion
    }
}
