// Sarthak Killedar
// 2026-11-07

using System;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Imaging;

namespace Onvif.Open.Core.Abstract.Interface.Imaging
{
    /// <summary>
    /// ONVIF Imaging service (ver20) operations for one video source.
    /// </summary>
    public interface IImagingService
    {
        #region Public Variables

        Uri ServiceUri { get; }

        #endregion

        #region Public Methods

        Task<ImagingSettings> GetImagingSettingsAsync(string videoSourceToken, CancellationToken cancellationToken = default);

        /// <summary>Sends only the properties of <paramref name="settings"/> that are set.</summary>
        Task SetImagingSettingsAsync(string videoSourceToken, ImagingSettings settings, bool forcePersistence = true, CancellationToken cancellationToken = default);

        Task<ImagingOptions> GetOptionsAsync(string videoSourceToken, CancellationToken cancellationToken = default);

        /// <summary>Continuous focus move at <paramref name="speed"/> (negative: near, positive: far) until stopped.</summary>
        Task MoveFocusContinuousAsync(string videoSourceToken, double speed, CancellationToken cancellationToken = default);

        Task MoveFocusAbsoluteAsync(string videoSourceToken, double position, double? speed = null, CancellationToken cancellationToken = default);

        Task MoveFocusRelativeAsync(string videoSourceToken, double distance, double? speed = null, CancellationToken cancellationToken = default);

        Task StopFocusAsync(string videoSourceToken, CancellationToken cancellationToken = default);

        Task<FocusStatus> GetFocusStatusAsync(string videoSourceToken, CancellationToken cancellationToken = default);

        #endregion
    }
}
