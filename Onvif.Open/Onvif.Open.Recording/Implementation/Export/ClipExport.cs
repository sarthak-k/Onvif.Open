// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Recording;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Format;

namespace Onvif.Open.Recording.Implementation.Export
{
    /// <summary>
    /// Cuts a clip out of the recordings of a camera, across segment boundaries, from the local storage or the archive:
    /// <code>
    /// await using var file = File.Create("clip.ts");
    /// await ClipExport.WriteAsync(manager, "front-door", from, to, file);
    /// </code>
    /// The clip starts at the last key frame at or before <c>from</c> (so it plays from its first frame) and ends after
    /// <c>to</c>; gaps between recordings stay gaps. Recordings must be MPEG-TS (the default format).
    /// </summary>
    public static class ClipExport
    {
        #region Public Methods

        /// <summary>Writes the clip as MPEG-TS; returns the number of video frames (0 when nothing was recorded then).</summary>
        public static async Task<long> WriteAsync(IRecordingManager manager, string cameraId, DateTimeOffset from, DateTimeOffset to, Stream output, CancellationToken cancellationToken = default)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (to <= from)
            {
                throw new ArgumentOutOfRangeException(nameof(to), "The end of the clip must be after its start.");
            }

            var segments = manager.GetSegments(cameraId)
                .Where(s => s.StartTime <= to && s.LastWriteTime >= from && s.FileName.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var writer = new ClipWriter(output, from, to);

            foreach (var segment in segments)
            {
                using (var input = await manager.OpenSegmentAsync(segment, cancellationToken).ConfigureAwait(false))
                {
                    if (input == null)
                    {
                        continue;
                    }

                    if (!await writer.AddSegmentAsync(segment, input, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }
                }
            }

            await writer.CompleteAsync(cancellationToken).ConfigureAwait(false);
            return writer.FramesWritten;
        }

        #endregion

        /// <summary>Maps each segment's PTS onto wall-clock time and re-muxes the frames in range onto one timeline.</summary>
        private sealed class ClipWriter
        {
            public ClipWriter(Stream output, DateTimeOffset from, DateTimeOffset to)
            {
                _output = output;
                _from = from;
                _to = to;
            }

            private readonly Stream _output;
            private readonly DateTimeOffset _from;
            private readonly DateTimeOffset _to;
            private readonly MemoryStream _buffer = new MemoryStream();

            // Frames of the current GOP before "from": the clip starts at its key frame.
            private readonly List<(DateTimeOffset Time, DemuxedFrame Frame)> _pending = new List<(DateTimeOffset, DemuxedFrame)>();

            private ISegmentMuxer _muxer;
            private VideoCodec _codec;
            private DateTimeOffset _clipStart;
            private bool _done;

            public long FramesWritten { get; private set; }

            /// <summary>False once the clip is complete (no further segments needed).</summary>
            public async Task<bool> AddSegmentAsync(RecordingSegment segment, Stream input, CancellationToken cancellationToken)
            {
                var demuxer = new MpegTsDemuxer();
                long? firstVideoPts = null;

                await foreach (var frame in demuxer.ReadAsync(input, cancellationToken).ConfigureAwait(false))
                {
                    if (!frame.IsAudio)
                    {
                        firstVideoPts ??= frame.Pts;
                    }

                    if (!firstVideoPts.HasValue)
                    {
                        continue;
                    }

                    // The segment's start time is the time of its first video frame.
                    var time = segment.StartTime + TimeSpan.FromTicks((frame.Pts - firstVideoPts.Value) * TimeSpan.TicksPerSecond / 90000);

                    if (time > _to)
                    {
                        if (!frame.IsAudio)
                        {
                            _done = true;
                            break;
                        }

                        continue;
                    }

                    if (_muxer == null)
                    {
                        if (!frame.IsAudio && frame.IsKeyFrame)
                        {
                            // A new GOP: what came before "from" is not needed.
                            _pending.Clear();
                        }

                        if (_pending.Count == 0 && (frame.IsAudio || !frame.IsKeyFrame))
                        {
                            // Wait for a key frame to start the GOP.
                            continue;
                        }

                        _pending.Add((time, frame));

                        if (time >= _from && !frame.IsAudio)
                        {
                            Start(demuxer.Codec);
                            foreach (var (pendingTime, pendingFrame) in _pending)
                            {
                                Write(pendingTime, pendingFrame);
                            }

                            _pending.Clear();
                            await FlushAsync(cancellationToken).ConfigureAwait(false);
                        }

                        continue;
                    }

                    if (!frame.IsAudio && demuxer.Codec != _codec)
                    {
                        // The camera changed its codec: one clip cannot hold both.
                        _done = true;
                        break;
                    }

                    Write(time, frame);
                    await FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                return !_done;
            }

            public async Task CompleteAsync(CancellationToken cancellationToken)
            {
                _muxer?.Dispose();
                await FlushAsync(cancellationToken).ConfigureAwait(false);
                await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            private void Start(VideoCodec codec)
            {
                _codec = codec;
                _clipStart = _pending[0].Time;
                var audio = _pending.FirstOrDefault(p => p.Frame.IsAudio).Frame?.Audio;
                _muxer = SegmentFormats.MpegTs.CreateMuxer(_buffer, codec, audio);
            }

            private void Write(DateTimeOffset time, DemuxedFrame frame)
            {
                var timestamp = time - _clipStart;

                if (frame.IsAudio)
                {
                    foreach (var unit in frame.AacAccessUnits())
                    {
                        _muxer.Write(new AudioFrame(frame.Audio, unit, 0, timestamp));
                    }

                    return;
                }

                _muxer.Write(new VideoFrame(_codec, frame.Data, frame.IsKeyFrame, 0, timestamp));
                FramesWritten++;
            }

            private async Task FlushAsync(CancellationToken cancellationToken)
            {
                if (_buffer.Length == 0)
                {
                    return;
                }

                await _output.WriteAsync(_buffer.GetBuffer(), 0, (int)_buffer.Length, cancellationToken).ConfigureAwait(false);
                _buffer.SetLength(0);
            }
        }
    }
}
