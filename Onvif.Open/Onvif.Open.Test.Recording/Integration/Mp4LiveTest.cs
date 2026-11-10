// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Mp4;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Low-latency live: H.264 frames as fragmented MP4, one fragment per frame, fanned out to viewers.
    /// </summary>
    public sealed class Mp4LiveTest
    {
        private static List<byte[]> Drain(Mp4LiveStream.Viewer viewer)
        {
            var fragments = new List<byte[]>();
            while (viewer.Fragments.TryRead(out var fragment))
            {
                fragments.Add(fragment);
            }

            return fragments;
        }

        [SkippableFact]
        public async Task ViewersShouldGetDecodableMp4StartingAtAKeyFrame()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            var video = RealMedia.Frames.OfType<VideoFrame>().ToList();
            var stream = new Mp4LiveStream(viewerBacklog: 1000);
            Assert.False(await stream.WaitUntilReadyAsync(TimeSpan.FromMilliseconds(10)));

            // Joining mid-GOP: frames before the first key frame are skipped. 1.5 s in, the viewer joins and gets the
            // frames since the key frame at 1 s at once.
            foreach (var frame in video.Where(f => f.Timestamp >= TimeSpan.FromMilliseconds(500) && f.Timestamp < TimeSpan.FromMilliseconds(1500)))
            {
                stream.Add(frame);
            }

            Assert.True(await stream.WaitUntilReadyAsync(TimeSpan.Zero));
            Assert.StartsWith("avc1.", stream.CodecString);
            using (var viewer = stream.Watch())
            {
                var joined = Drain(viewer);
                Assert.Equal(1 + 13, joined.Count);
                Assert.Equal(1, stream.ViewerCount);

                foreach (var frame in video.Where(f => f.Timestamp >= TimeSpan.FromMilliseconds(1500)))
                {
                    stream.Add(frame);
                }

                var all = joined.Concat(Drain(viewer)).ToList();
                Assert.Equal(1 + video.Count(f => f.Timestamp >= TimeSpan.FromSeconds(1)), all.Count);

                using (var temp = new TempDirectory())
                {
                    var path = Path.Combine(temp.Path, "live.mp4");
                    File.WriteAllBytes(path, all.SelectMany(f => f).ToArray());
                    Assert.Equal(string.Empty, await RealMedia.DecodeErrorsAsync(path));
                    var probe = await RealMedia.ProbeAsync(path);
                    Assert.Equal(new[] { "h264" }, probe.Streams);
                    Assert.Equal(9.0, probe.Duration, 1);
                    var size = await RealMedia.RunAsync("ffprobe", $"-v error -show_entries stream=width,height -of csv=p=0 \"{path}\"");
                    Assert.Equal("320,240", size.Output.Trim());
                }
            }

            Assert.Equal(0, stream.ViewerCount);
        }

        [SkippableFact]
        public void SlowViewersAndNewStreamsShouldDropViewers()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            var video = RealMedia.Frames.OfType<VideoFrame>().ToList();
            var stream = new Mp4LiveStream(viewerBacklog: 10);
            stream.Add(video[0]);
            var slow = stream.Watch();
            var fast = stream.Watch();

            // The slow viewer reads nothing and is dropped once its backlog is full; the other keeps up.
            foreach (var frame in video.Skip(1).Take(20))
            {
                stream.Add(frame);
                Drain(fast);
            }

            Assert.Equal(1, stream.ViewerCount);
            Drain(slow);
            Assert.True(slow.Fragments.Completion.IsCompleted);
            Assert.False(fast.Fragments.Completion.IsCompleted);

            // The camera reconnected (time starts over): every viewer is dropped, and the stream waits for a key frame.
            stream.Add(video[3]);
            Drain(fast);
            Assert.True(fast.Fragments.Completion.IsCompleted);
            Assert.Equal(0, stream.ViewerCount);
            using (var early = stream.Watch())
            {
                Assert.True(early.Fragments.Completion.IsCompleted);
            }

            stream.Add(video[0]);
            using (var again = stream.Watch())
            {
                Assert.Equal(2, Drain(again).Count);
            }
        }

        [Fact]
        public void OtherCodecsShouldUseHls()
        {
            var stream = new Mp4LiveStream();
            stream.Add(new VideoFrame(VideoCodec.H265, new byte[] { 0, 0, 0, 1, 0x26, 1 }, true, 0, TimeSpan.Zero));
            Assert.False(stream.IsSupported);
            Assert.False(stream.WaitUntilReadyAsync(TimeSpan.Zero).Result);
        }

        [Fact]
        public void SpsShouldGiveTheSizeAndCodec()
        {
            // 1920x1080 High profile, level 4.0 (cropped from 1088), as cameras send it.
            var sps = Convert.FromHexString("6764002AACD940780227E5C04400000303E80000EA6038000E4E1C0FFE30C6");
            var parsed = H264Sps.Parse(sps);
            Assert.NotNull(parsed);
            Assert.Equal("avc1.64002a", parsed.CodecString);
            Assert.Equal((1920, 1080), (parsed.Width, parsed.Height));
            Assert.Null(H264Sps.Parse(new byte[] { 0x68, 1, 2, 3 }));
        }
    }
}
