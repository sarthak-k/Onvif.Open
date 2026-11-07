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
    /// Pan/tilt/zoom of a camera, on its first PTZ-enabled media profile. Throws
    /// <see cref="NotSupportedException"/> when the camera has no PTZ.
    /// </summary>
    public interface ICameraPtz
    {
        #region Public Methods

        /// <summary>
        /// Moves at the given speeds (-1..1, 0 = no movement on that axis). With <paramref name="duration"/> the camera
        /// is stopped again after it; without, it moves until <see cref="StopAsync"/>.
        /// </summary>
        Task MoveAsync(double pan, double tilt, double zoom, TimeSpan? duration = null, CancellationToken cancellationToken = default);

        Task StopAsync(CancellationToken cancellationToken = default);

        /// <summary>Moves by an offset from the current position (generic relative space).</summary>
        Task MoveByAsync(PtzVector translation, CancellationToken cancellationToken = default);

        /// <summary>Moves to an absolute position (generic space: pan/tilt -1..1, zoom 0..1).</summary>
        Task MoveToAsync(PtzVector position, CancellationToken cancellationToken = default);

        Task<PtzStatus> GetStatusAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PtzPreset>> GetPresetsAsync(CancellationToken cancellationToken = default);

        /// <summary>Moves to the preset with this name or token.</summary>
        Task GoToPresetAsync(string nameOrToken, CancellationToken cancellationToken = default);

        /// <summary>Saves the current position under <paramref name="name"/> (replacing a preset with that name). Returns its token.</summary>
        Task<string> SavePresetAsync(string name, CancellationToken cancellationToken = default);

        /// <summary>Removes the preset with this name or token; false when there is none.</summary>
        Task<bool> RemovePresetAsync(string nameOrToken, CancellationToken cancellationToken = default);

        Task GoHomeAsync(CancellationToken cancellationToken = default);

        /// <summary>Makes the current position the home position.</summary>
        Task SetHomeAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
