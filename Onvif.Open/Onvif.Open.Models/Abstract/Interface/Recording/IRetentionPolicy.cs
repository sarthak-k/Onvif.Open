// Sarthak Killedar
// 2026-11-07

using System.Collections.Generic;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Core.Abstract.Interface.Recording
{
    /// <summary>
    /// Decides which completed segments are deleted to keep storage under control.
    /// Segments that are still being written are never offered.
    /// </summary>
    public interface IRetentionPolicy
    {
        #region Public Methods

        IEnumerable<RecordingSegment> SelectForDeletion(RetentionContext context);

        #endregion
    }
}
