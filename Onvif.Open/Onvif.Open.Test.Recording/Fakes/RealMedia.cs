// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Test.Recording.Fakes
{
    /// <summary>
    /// Real H.264 (25 fps, a key frame every second) and AAC (16 kHz mono) made by ffmpeg, as frames on one timeline,
    /// and ffmpeg/ffprobe to check what the code under test produced. Tests using it skip where ffmpeg is missing.
    /// </summary>
    internal static class RealMedia
    {
        #region Private Variables

        private static readonly Lazy<bool> Available = new Lazy<bool>(() =>
        {
            try
            {
                return RunAsync("ffmpeg", "-hide_banner -version").GetAwaiter().GetResult().ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        });

        private static readonly Lazy<IReadOnlyList<MediaFrame>> Frames10s = new Lazy<IReadOnlyList<MediaFrame>>(() => Generate(10).GetAwaiter().GetResult());

        #endregion

        #region Public Variables

        /// <summary>Writes a frame into a recording, shifted so the recording starts at zero.</summary>
        public static void Write(Onvif.Open.Recording.Implementation.Format.ISegmentMuxer muxer, MediaFrame frame, TimeSpan offset)
        {
            switch (frame)
            {
                case VideoFrame v:
                    muxer.Write(new VideoFrame(v.Codec, v.Data, v.IsKeyFrame, v.RtpTimestamp, v.Timestamp - offset));
                    break;
                case AudioFrame a:
                    muxer.Write(a.WithTimestamp(a.Timestamp - offset));
                    break;
            }
        }

        /// <summary>The 10 s of real media as two 5 s MPEG-TS recordings of a camera from <paramref name="start"/>, in a storage folder.</summary>
        public static Onvif.Open.Recording.Implementation.Storage.FileSystemRecordingStorage WriteRecordings(string directory, string cameraId, DateTimeOffset start)
        {
            var storage = new Onvif.Open.Recording.Implementation.Storage.FileSystemRecordingStorage(directory);

            foreach (var part in new[] { 0, 5 })
            {
                var frames = Frames.Where(f => f.Timestamp.TotalSeconds >= part && f.Timestamp.TotalSeconds < part + 5).ToList();
                var first = frames.OfType<VideoFrame>().First().Timestamp;

                using (var stream = storage.CreateSegment(cameraId, start.AddSeconds(part), ".ts", out var path))
                {
                    using (var muxer = Onvif.Open.Recording.Implementation.Format.SegmentFormats.MpegTs.CreateMuxer(stream, Onvif.Open.Core.Streaming.VideoCodec.H264, Aac16k))
                    {
                        foreach (var frame in frames.SkipWhile(f => f is AudioFrame))
                        {
                            Write(muxer, frame, first);
                        }
                    }

                    stream.Dispose();
                    File.SetLastWriteTimeUtc(path, start.AddSeconds(part + 5).UtcDateTime);
                }
            }

            return storage;
        }

        public static bool IsAvailable => Available.Value;

        /// <summary>10 seconds of video and audio, ordered by timestamp.</summary>
        public static IReadOnlyList<MediaFrame> Frames => Frames10s.Value;

        public static AudioFormat Aac16k => FakeLiveVideoStream.Aac16k;

        #endregion

        #region Public Methods

        public static async Task<(int ExitCode, string Output, string Error)> RunAsync(string file, string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false }))
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();

                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    throw new TimeoutException($"{file} did not finish within 60 seconds.");
                }

                return (process.ExitCode, await output, await error);
            }
        }

        /// <summary>Errors ffmpeg reports while decoding <paramref name="path"/> (empty when it decodes cleanly).</summary>
        public static async Task<string> DecodeErrorsAsync(string path, string inputOptions = "")
        {
            var decoded = await RunAsync("ffmpeg", $"-hide_banner -nostdin -v error {inputOptions} -i \"{path}\" -f null -");
            return decoded.ExitCode == 0 ? decoded.Error.Trim() : $"exit {decoded.ExitCode}: {decoded.Error}";
        }

        /// <summary>Codec lines (e.g. "h264", "aac,16000,1") and the longest stream duration of <paramref name="path"/>.</summary>
        public static async Task<(IReadOnlyList<string> Streams, double Duration)> ProbeAsync(string path, string inputOptions = "")
        {
            var streams = await RunAsync("ffprobe", $"-v error {inputOptions} -show_entries stream=codec_name,sample_rate,channels -of csv=p=0 \"{path}\"");
            var format = await RunAsync("ffprobe", $"-v error {inputOptions} -show_entries format=duration -of csv=p=0 \"{path}\"");
            double.TryParse(format.Output.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var duration);
            return (streams.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().TrimEnd(',')).ToList(), duration);
        }

        #endregion

        #region Private Methods

        private static async Task<IReadOnlyList<MediaFrame>> Generate(int seconds)
        {
            var directory = Path.Combine(Path.GetTempPath(), "onvif-open-media", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);

            try
            {
                var video = Path.Combine(directory, "video.h264");
                var audio = Path.Combine(directory, "audio.aac");
                var made = await RunAsync("ffmpeg", $"-hide_banner -nostdin -v error -f lavfi -t {seconds} -i testsrc=size=320x240:rate=25 -f lavfi -t {seconds} -i sine=frequency=440:sample_rate=16000 " +
                                                   $"-map 0:v -c:v libx264 -g 25 -bf 0 -bsf:v h264_metadata=aud=insert -f h264 \"{video}\" " +
                                                   $"-map 1:a -c:a aac -ac 1 -f adts \"{audio}\"");

                if (made.ExitCode != 0)
                {
                    throw new InvalidOperationException(made.Error);
                }

                var frames = new List<MediaFrame>();
                frames.AddRange(AccessUnits(File.ReadAllBytes(video)).Select((unit, i) => (MediaFrame)new VideoFrame(VideoCodec.H264, unit, IsKeyFrame(unit), (uint)(i * 3600), TimeSpan.FromMilliseconds(i * 40))));
                frames.AddRange(AdtsFrames(File.ReadAllBytes(audio)).Select((unit, i) => (MediaFrame)new AudioFrame(Aac16k, unit, (uint)(i * 1024), TimeSpan.FromTicks(i * 1024L * TimeSpan.TicksPerSecond / 16000))));
                return frames.OrderBy(f => f.Timestamp).ThenBy(f => f is AudioFrame ? 1 : 0).ToList();
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>Annex-B access units, split at the access unit delimiters ffmpeg inserted.</summary>
        private static List<byte[]> AccessUnits(byte[] annexB)
        {
            var starts = new List<int>();
            for (var i = 0; i + 4 < annexB.Length; i++)
            {
                if (annexB[i] == 0 && annexB[i + 1] == 0 && annexB[i + 2] == 0 && annexB[i + 3] == 1 && (annexB[i + 4] & 0x1F) == 9)
                {
                    starts.Add(i);
                }
            }

            starts.Add(annexB.Length);
            return Enumerable.Range(0, starts.Count - 1).Select(i => annexB.Skip(starts[i]).Take(starts[i + 1] - starts[i]).ToArray()).ToList();
        }

        private static bool IsKeyFrame(byte[] accessUnit)
        {
            for (var i = 0; i + 4 < accessUnit.Length; i++)
            {
                if (accessUnit[i] == 0 && accessUnit[i + 1] == 0 && accessUnit[i + 2] == 1 && (accessUnit[i + 3] & 0x1F) == 5)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Raw AAC access units of an ADTS file (headers without CRC).</summary>
        private static List<byte[]> AdtsFrames(byte[] adts)
        {
            var frames = new List<byte[]>();
            for (var offset = 0; offset + 7 <= adts.Length;)
            {
                var length = ((adts[offset + 3] & 0x03) << 11) | (adts[offset + 4] << 3) | (adts[offset + 5] >> 5);
                frames.Add(adts.Skip(offset + 7).Take(length - 7).ToArray());
                offset += length;
            }

            return frames;
        }

        #endregion
    }
}
