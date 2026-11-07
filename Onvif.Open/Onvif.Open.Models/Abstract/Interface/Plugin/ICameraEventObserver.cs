// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Events;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Receives the cameras' own ONVIF events (motion, tampering, digital inputs, analytics rules, ...).
    /// </summary>
    public interface ICameraEventObserver : IPlugin
    {
        #region Public Methods

        Task OnCameraEventAsync(CameraEvent cameraEvent, CancellationToken cancellationToken);

        #endregion
    }
}
