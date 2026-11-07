// Sarthak Killedar
// 2026-11-07

using System.Globalization;

namespace Onvif.Open.Core.Ptz
{
    /// <summary>
    /// Pan, tilt and zoom values in the device's default (generic) spaces: positions and velocities -1..1 for
    /// pan/tilt, zoom position 0..1 and zoom velocity -1..1. A null component is left out (not moved).
    /// </summary>
    public sealed class PtzVector
    {
        #region Constructor

        public PtzVector(double? pan = null, double? tilt = null, double? zoom = null)
        {
            Pan = pan;
            Tilt = tilt;
            Zoom = zoom;
        }

        #endregion

        #region Public Variables

        public double? Pan { get; }
        public double? Tilt { get; }
        public double? Zoom { get; }

        public bool HasPanTilt => Pan.HasValue || Tilt.HasValue;

        #endregion

        #region Public Methods

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "pan={0} tilt={1} zoom={2}", Pan, Tilt, Zoom);
        }

        #endregion
    }
}
