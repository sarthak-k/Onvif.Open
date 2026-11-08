// Sarthak Killedar
// 2026-11-07

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
        [SkippableFact]
        public async Task VideoWithAacAudioShouldDecodeWithoutErrors()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            using (var temp = new TempDirectory())
            {
                var output = Path.Combine(temp.Path, "out.ts");

                using (var file = File.Create(output))
                using (var muxer = SegmentFormats.MpegTs.CreateMuxer(file, VideoCodec.H264, RealMedia.Aac16k))
                {
                    // In timeline order, as the recorder receives them.
                    foreach (var frame in RealMedia.Frames.Where(f => f.Timestamp.TotalSeconds < 2))
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

                Assert.Equal(string.Empty, await RealMedia.DecodeErrorsAsync(output));

                var probe = await RealMedia.ProbeAsync(output);
                Assert.Contains("h264", probe.Streams);
                Assert.Contains("aac,16000,1", probe.Streams);
                Assert.InRange(probe.Duration, 1.8, 2.2);
            }
        }
    }
}
