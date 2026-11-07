// Sarthak Killedar
// 2026-11-07

namespace Onvif.Open.Core.Abstract.Interface.Recording
{
    /// <summary>
    /// Decides when the current segment file is closed and a new one started.
    /// It is asked at every key frame, so every segment starts with a key frame and plays on its own.
    /// </summary>
    public interface IRotationPolicy
    {
        #region Public Methods

        bool ShouldRotate(ISegmentProgress segment);

        #endregion
    }
}
