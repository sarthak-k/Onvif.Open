// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Plugin;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Notified when a camera's recorder changes state, e.g. Recording → Reconnecting when the camera
    /// goes offline and Connecting → Recording when it is back.
    /// </summary>
    public interface ICameraStateObserver : IPlugin
    {
        #region Public Methods

        Task OnCameraStateChangedAsync(CameraStateChange change, CancellationToken cancellationToken);

        #endregion
    }
}
