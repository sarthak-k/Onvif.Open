// Sarthak Killedar
// 2026-11-07

using System;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Imaging;

namespace Onvif.Open.Core.Abstract.Interface.Imaging
{
    /// <summary>
    /// Image settings and focus of the camera's (first profile's) video source. Throws
    /// <see cref="NotSupportedException"/> when the camera has no Imaging service.
    /// </summary>
    public interface ICameraImaging
    {
        #region Public Methods

        Task<ImagingSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

        /// <summary>Changes only the properties that are set, e.g. <c>new ImagingSettings { IrCutFilter = IrCutFilterMode.Auto }</c>.</summary>
        Task UpdateAsync(ImagingSettings changes, CancellationToken cancellationToken = default);

        Task<ImagingOptions> GetOptionsAsync(CancellationToken cancellationToken = default);

        /// <summary>Moves the focus at <paramref name="speed"/> (-1 near .. 1 far); with <paramref name="duration"/> it stops afterwards.</summary>
        Task MoveFocusAsync(double speed, TimeSpan? duration = null, CancellationToken cancellationToken = default);

        Task FocusToAsync(double position, CancellationToken cancellationToken = default);

        Task StopFocusAsync(CancellationToken cancellationToken = default);

        Task<FocusStatus> GetFocusStatusAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
