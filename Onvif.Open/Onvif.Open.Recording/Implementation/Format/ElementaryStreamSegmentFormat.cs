// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Recording.Implementation.Format
{
    internal sealed class ElementaryStreamSegmentFormat : ISegmentFormat
    {
        #region Public Methods

        public bool Supports(VideoCodec codec)
        {
            return codec == VideoCodec.H264 || codec == VideoCodec.H265;
        }

        public string GetFileExtension(VideoCodec codec)
        {
            return codec == VideoCodec.H265 ? ".h265" : ".h264";
        }

        public ISegmentMuxer CreateMuxer(Stream output, VideoCodec codec)
        {
            return new Muxer(output ?? throw new ArgumentNullException(nameof(output)));
        }

        #endregion

        private sealed class Muxer : ISegmentMuxer
        {
            private readonly Stream _output;

            public Muxer(Stream output)
            {
                _output = output;
            }

            public void Write(VideoFrame frame)
            {
                _output.Write(frame.Data.Span);
            }

            public void Dispose()
            {
                _output.Flush();
            }
        }
    }
}
