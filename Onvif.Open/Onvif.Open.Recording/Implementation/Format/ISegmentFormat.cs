// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Recording.Implementation.Format
{
    /// <summary>
    /// File format of recorded segments (strategy). See <see cref="SegmentFormats"/> for the built-in ones.
    /// </summary>
    public interface ISegmentFormat
    {
        #region Public Methods

        bool Supports(VideoCodec codec);

        /// <summary>File extension including the dot, e.g. ".ts".</summary>
        string GetFileExtension(VideoCodec codec);

        ISegmentMuxer CreateMuxer(Stream output, VideoCodec codec);

        /// <summary>Whether audio of <paramref name="audio"/>'s format can be written along with the video. Default: no.</summary>
        bool SupportsAudio(AudioFormat audio)
        {
            return false;
        }

        /// <summary>A muxer for video and, when <paramref name="audio"/> is supported, audio.</summary>
        ISegmentMuxer CreateMuxer(Stream output, VideoCodec codec, AudioFormat audio)
        {
            return CreateMuxer(output, codec);
        }

        #endregion
    }

    /// <summary>
    /// Writes the frames of one segment into its output stream.
    /// </summary>
    public interface ISegmentMuxer : IDisposable
    {
        #region Public Methods

        void Write(VideoFrame frame);

        /// <summary>Writes an audio frame (muxers created without audio ignore it).</summary>
        void Write(AudioFrame frame)
        {
        }

        #endregion
    }
}
