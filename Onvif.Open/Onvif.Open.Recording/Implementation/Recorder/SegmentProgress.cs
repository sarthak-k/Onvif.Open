// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Abstract.Interface.Recording;

namespace Onvif.Open.Recording.Implementation.Recorder
{
    internal sealed class SegmentProgress : ISegmentProgress
    {
        #region Public Variables

        public string CameraId { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset Now { get; set; }
        public TimeSpan Duration { get; set; }
        public long Size { get; set; }
        public int FrameCount { get; set; }

        #endregion
    }
}
