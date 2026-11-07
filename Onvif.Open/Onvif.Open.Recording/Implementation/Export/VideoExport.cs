// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Recording.Implementation.Format;

namespace Onvif.Open.Recording.Implementation.Export
{
    /// <summary>
    /// Writes a video stream into a playable file, e.g. a clip replayed from a camera's SD card:
    /// <code>
    /// await using var replay = await camera.InitializeRecordings().StartReplayAsync(token, from, new ReplayOptions { To = to, RealTime = false });
    /// await using var file = File.Create("clip.ts");
    /// await VideoExport.WriteAsync(replay, file);
    /// </code>
    /// </summary>
    public static class VideoExport
    {
        #region Public Methods

        /// <summary>
        /// Writes every frame from the first key frame until the stream ends, asynchronously (safe for HTTP responses).
        /// Returns the number of frames written.
        /// </summary>
        public static async Task<long> WriteAsync(ILiveVideoStream stream, Stream output, ISegmentFormat format = null, CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            format = format ?? SegmentFormats.MpegTs;

            if (!format.Supports(stream.Codec))
            {
                throw new NotSupportedException($"The {stream.Codec} stream cannot be written in this format.");
            }

            // Muxers write synchronously: mux each frame into a buffer and copy it out asynchronously.
            var buffer = new MemoryStream();
            var frames = 0L;

            using (var muxer = format.CreateMuxer(buffer, stream.Codec))
            {
                await foreach (var frame in stream.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (frames == 0 && !frame.IsKeyFrame)
                    {
                        continue;
                    }

                    muxer.Write(frame);
                    frames++;
                    await FlushAsync(buffer, output, cancellationToken).ConfigureAwait(false);
                }
            }

            await FlushAsync(buffer, output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return frames;
        }

        /// <summary>File extension of the export, e.g. ".ts".</summary>
        public static string GetFileExtension(ILiveVideoStream stream, ISegmentFormat format = null)
        {
            return (format ?? SegmentFormats.MpegTs).GetFileExtension(stream.Codec);
        }

        #endregion

        #region Private Methods

        private static async Task FlushAsync(MemoryStream buffer, Stream output, CancellationToken cancellationToken)
        {
            if (buffer.Length == 0)
            {
                return;
            }

            await output.WriteAsync(buffer.GetBuffer(), 0, (int)buffer.Length, cancellationToken).ConfigureAwait(false);
            buffer.SetLength(0);
        }

        #endregion
    }
}
