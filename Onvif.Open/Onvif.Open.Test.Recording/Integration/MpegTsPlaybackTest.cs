// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Muxes real H.264 and AAC (made by ffmpeg) and lets ffmpeg decode the result: the strongest check that the
    /// recordings play. Skipped where ffmpeg is not installed.
    /// </summary>
    public class MpegTsPlaybackTest
    {
        #region Private Methods

        private static async Task<(int ExitCode, string Output, string Error)> Run(string file, string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false }))
            using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60)))
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

        private static bool HasFfmpeg()
        {
            try
            {
                return Run("ffmpeg", "-hide_banner -version").GetAwaiter().GetResult().ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
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

        #region Public Methods

        [SkippableFact]
        public async Task VideoWithAacAudioShouldDecodeWithoutErrors()
        {
            Skip.IfNot(HasFfmpeg(), "ffmpeg is not installed.");

            using (var temp = new TempDirectory())
            {
                var video = Path.Combine(temp.Path, "video.h264");
                var audio = Path.Combine(temp.Path, "audio.aac");
                var output = Path.Combine(temp.Path, "out.ts");

                var made = await Run("ffmpeg", $"-hide_banner -nostdin -v error -f lavfi -t 2 -i testsrc=size=320x240:rate=25 -f lavfi -t 2 -i sine=frequency=440:sample_rate=16000 " +
                                               $"-map 0:v -c:v libx264 -g 25 -bf 0 -bsf:v h264_metadata=aud=insert -f h264 \"{video}\" " +
                                               $"-map 1:a -c:a aac -ac 1 -f adts \"{audio}\"");
                Assert.True(made.ExitCode == 0, made.Error);

                var frames = new List<MediaFrame>();
                frames.AddRange(AccessUnits(File.ReadAllBytes(video)).Select((unit, i) => (MediaFrame)new VideoFrame(VideoCodec.H264, unit, IsKeyFrame(unit), (uint)(i * 3600), TimeSpan.FromMilliseconds(i * 40))));
                frames.AddRange(AdtsFrames(File.ReadAllBytes(audio)).Select((unit, i) => (MediaFrame)new AudioFrame(FakeLiveVideoStream.Aac16k, unit, (uint)(i * 1024), TimeSpan.FromTicks(i * 1024L * TimeSpan.TicksPerSecond / 16000))));

                using (var file = File.Create(output))
                using (var muxer = SegmentFormats.MpegTs.CreateMuxer(file, VideoCodec.H264, FakeLiveVideoStream.Aac16k))
                {
                    // In timeline order, as the recorder receives them.
                    foreach (var frame in frames.OrderBy(f => f.Timestamp))
                    {
                        switch (frame)
                        {
                            case VideoFrame v:
                                muxer.Write(v);
                                break;
                            case AudioFrame a:
                                muxer.Write(a);
                                break;
                        }
                    }
                }

                var decoded = await Run("ffmpeg", $"-hide_banner -nostdin -v error -i \"{output}\" -f null -");
                Assert.True(decoded.ExitCode == 0 && decoded.Error.Trim().Length == 0, decoded.Error);

                var probe = await Run("ffprobe", $"-v error -show_entries stream=codec_name,sample_rate,channels -of csv=p=0 \"{output}\"");
                var streams = probe.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
                Assert.Contains("h264", streams);
                Assert.Contains("aac,16000,1", streams);

                var durations = await Run("ffprobe", $"-v error -show_entries stream=duration -of csv=p=0 \"{output}\"");
                Assert.All(durations.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries), d => Assert.InRange(double.Parse(d.Trim(), System.Globalization.CultureInfo.InvariantCulture), 1.8, 2.2));
            }
        }

        #endregion
    }
}
