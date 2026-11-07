// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Recording.Implementation.Format
{
    internal sealed class MpegTsSegmentFormat : ISegmentFormat
    {
        #region Public Methods

        public bool Supports(VideoCodec codec)
        {
            return codec == VideoCodec.H264 || codec == VideoCodec.H265;
        }

        public string GetFileExtension(VideoCodec codec)
        {
            return ".ts";
        }

        public ISegmentMuxer CreateMuxer(Stream output, VideoCodec codec)
        {
            if (!Supports(codec))
            {
                throw new NotSupportedException($"MPEG-TS recording does not support codec '{codec}'.");
            }

            return new MpegTsMuxer(output ?? throw new ArgumentNullException(nameof(output)), codec);
        }

        #endregion
    }
}
