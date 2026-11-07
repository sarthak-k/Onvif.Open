// Sarthak Killedar
// 2026-11-07

namespace Onvif.Open.Recording.Implementation.Format
{
    public static class SegmentFormats
    {
        #region Public Variables

        /// <summary>
        /// MPEG transport stream (.ts): keeps timestamps, plays in VLC/ffplay/browsers (HLS) and
        /// remains readable when the recorder stops abruptly. Default.
        /// </summary>
        public static ISegmentFormat MpegTs { get; } = new MpegTsSegmentFormat();

        /// <summary>Raw Annex-B elementary stream (.h264 / .h265), without timestamps.</summary>
        public static ISegmentFormat ElementaryStream { get; } = new ElementaryStreamSegmentFormat();

        #endregion
    }
}
