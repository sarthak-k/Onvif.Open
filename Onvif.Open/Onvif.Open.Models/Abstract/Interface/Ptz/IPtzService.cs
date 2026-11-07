// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Ptz;

namespace Onvif.Open.Core.Abstract.Interface.Ptz
{
    /// <summary>
    /// ONVIF PTZ service (ver20) operations for one media profile.
    /// </summary>
    public interface IPtzService
    {
        #region Public Variables

        Uri ServiceUri { get; }

        #endregion

        #region Public Methods

        /// <summary>Moves with <paramref name="velocity"/> until stopped, or until the device-side <paramref name="timeout"/>.</summary>
        Task ContinuousMoveAsync(string profileToken, PtzVector velocity, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

        Task RelativeMoveAsync(string profileToken, PtzVector translation, PtzVector speed = null, CancellationToken cancellationToken = default);

        Task AbsoluteMoveAsync(string profileToken, PtzVector position, PtzVector speed = null, CancellationToken cancellationToken = default);

        Task StopAsync(string profileToken, bool panTilt = true, bool zoom = true, CancellationToken cancellationToken = default);

        Task<PtzStatus> GetStatusAsync(string profileToken, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PtzPreset>> GetPresetsAsync(string profileToken, CancellationToken cancellationToken = default);

        Task GotoPresetAsync(string profileToken, string presetToken, PtzVector speed = null, CancellationToken cancellationToken = default);

        /// <summary>Saves the current position as a preset (a new one, or <paramref name="presetToken"/> to overwrite). Returns its token.</summary>
        Task<string> SetPresetAsync(string profileToken, string presetName, string presetToken = null, CancellationToken cancellationToken = default);

        Task RemovePresetAsync(string profileToken, string presetToken, CancellationToken cancellationToken = default);

        Task GotoHomePositionAsync(string profileToken, PtzVector speed = null, CancellationToken cancellationToken = default);

        Task SetHomePositionAsync(string profileToken, CancellationToken cancellationToken = default);

        #endregion
    }
}
