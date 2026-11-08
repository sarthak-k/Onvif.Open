// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Builder;
using Onvif.Open.Recording.Implementation.Export;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Recording.Implementation.Hls;
using Onvif.Open.Recording.Implementation.Policy;
using Onvif.Open.Recording.Implementation.Storage;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// Live HLS, HLS playback of recordings and clip export, checked by decoding the results with ffmpeg.
    /// </summary>
    public class HlsAndClipTest
    {
        #region Private Variables

        private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

        #endregion

        #region Private Methods

        private static void Write(ISegmentMuxer muxer, MediaFrame frame, TimeSpan offset)
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

        /// <summary>Writes the 10 s of real media as two 5 s recordings of "cam1" starting at <see cref="Start"/>.</summary>
        private static IRecordingManager Recordings(TempDirectory temp)
        {
            var storage = new FileSystemRecordingStorage(temp.Path);

            foreach (var part in new[] { 0, 5 })
            {
                var frames = RealMedia.Frames.Where(f => f.Timestamp.TotalSeconds >= part && f.Timestamp.TotalSeconds < part + 5).ToList();
                var first = frames.OfType<VideoFrame>().First().Timestamp;

                using (var stream = storage.CreateSegment("cam1", Start.AddSeconds(part), ".ts", out var path))
                {
                    using (var muxer = SegmentFormats.MpegTs.CreateMuxer(stream, VideoCodec.H264, RealMedia.Aac16k))
                    {
                        foreach (var frame in frames.SkipWhile(f => f is AudioFrame))
                        {
                            Write(muxer, frame, first);
                        }
                    }

                    stream.Dispose();
                    File.SetLastWriteTimeUtc(path, Start.AddSeconds(part + 5).UtcDateTime);
                }
            }

            return RecordingManagerBuilder.GetBuilder().SetStorage(storage).SetRetentionPolicy(RetentionPolicies.KeepAll).Build();
        }

        #endregion

        #region Public Methods

        [SkippableFact]
        public async Task DemuxerShouldReadBackWhatTheMuxerWrote()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            var frames = RealMedia.Frames.Where(f => f.Timestamp.TotalSeconds < 2).ToList();
            byte[] ts;

            using (var output = new MemoryStream())
            {
                using (var muxer = SegmentFormats.MpegTs.CreateMuxer(output, VideoCodec.H264, RealMedia.Aac16k))
                {
                    frames.ForEach(f => Write(muxer, f, TimeSpan.Zero));
                }

                ts = output.ToArray();
            }

            var demuxer = new MpegTsDemuxer();
            var read = new List<DemuxedFrame>();
            await foreach (var frame in demuxer.ReadAsync(new MemoryStream(ts)))
            {
                read.Add(frame);
            }

            var video = read.Where(f => !f.IsAudio).ToList();
            Assert.Equal(VideoCodec.H264, demuxer.Codec);
            Assert.Equal(50, video.Count);
            Assert.Equal(new[] { 0, 25 }, video.Select((f, i) => (f, i)).Where(x => x.f.IsKeyFrame).Select(x => x.i));
            Assert.Equal(TimeSpan.FromMilliseconds(40 * 49), TimeSpan.FromTicks((video[49].Pts - video[0].Pts) * TimeSpan.TicksPerSecond / 90000));
            Assert.Equal(frames.OfType<VideoFrame>().Select(f => f.Data.ToArray()), video.Select(f => f.Data));

            // Key frames can be decoded from the PAT before them.
            Assert.All(video.Where(f => f.IsKeyFrame), f => Assert.Equal(0, ((ts[f.Offset + 1] & 0x1F) << 8) | ts[f.Offset + 2]));

            var audio = read.Where(f => f.IsAudio).ToList();
            Assert.Equal(frames.OfType<AudioFrame>().Count(), audio.Count);
            Assert.Equal(16000, audio[0].Audio.SampleRate);
            Assert.Equal(frames.OfType<AudioFrame>().First().Data.ToArray(), audio[0].AacAccessUnits().Single());
        }

        [SkippableFact]
        public async Task LiveHlsShouldBePlayable()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            using (var temp = new TempDirectory())
            {
                var live = new HlsLiveStream(TimeSpan.FromSeconds(2), windowSize: 3);

                // Joining mid-GOP: frames before the next key frame are skipped.
                foreach (var frame in RealMedia.Frames.SkipWhile(f => f.Timestamp < TimeSpan.FromMilliseconds(500)))
                {
                    switch (frame)
                    {
                        case VideoFrame v:
                            live.Add(v);
                            break;
                        case AudioFrame a:
                            live.Add(a);
                            break;
                    }
                }

                var playlist = live.GetPlaylist(sequence => $"{sequence}.ts");

                // 1 s .. 10 s cut into 2 s segments; the window keeps the last three complete ones.
                Assert.Equal(3, live.SegmentCount);
                Assert.Contains("#EXT-X-TARGETDURATION:2", playlist);
                Assert.Contains("#EXT-X-MEDIA-SEQUENCE:1", playlist);
                Assert.Equal(3, Regex.Matches(playlist, "#EXTINF:2.000,").Count);
                Assert.False(live.TryGetSegment(0, out _));

                foreach (var sequence in new[] { 1, 2, 3 })
                {
                    Assert.True(live.TryGetSegment(sequence, out var data));
                    File.WriteAllBytes(Path.Combine(temp.Path, $"{sequence}.ts"), data);
                }

                var path = Path.Combine(temp.Path, "live.m3u8");
                File.WriteAllText(path, playlist);

                Assert.Equal(string.Empty, await RealMedia.DecodeErrorsAsync(path));
                var probe = await RealMedia.ProbeAsync(path);
                Assert.Contains("h264", probe.Streams);
                Assert.Contains("aac,16000,1", probe.Streams);
            }
        }

        [Fact]
        public void LiveHlsShouldMarkReconnectsAsDiscontinuities()
        {
            var live = new HlsLiveStream(TimeSpan.FromMilliseconds(200), windowSize: 6);

            void Gop(int firstIndex)
            {
                live.Add(FakeLiveVideoStream.Frame(firstIndex, true));
                for (var i = 1; i < 5; i++)
                {
                    live.Add(FakeLiveVideoStream.Frame(firstIndex + i, false));
                }
            }

            Gop(0);
            Gop(5);

            // The camera reconnected: timestamps start again.
            Gop(0);
            Gop(5);
            Gop(10);

            // 0-200 ms, 200-360 ms (cut by the reconnect), then 0-200 ms and 200-400 ms of the new connection.
            var playlist = live.GetPlaylist(sequence => $"{sequence}.ts");
            Assert.Equal(4, live.SegmentCount);
            Assert.Single(Regex.Matches(playlist, "#EXT-X-DISCONTINUITY"));
            Assert.True(playlist.IndexOf("#EXT-X-DISCONTINUITY", StringComparison.Ordinal) > playlist.IndexOf("1.ts", StringComparison.Ordinal));
            Assert.True(playlist.IndexOf("#EXT-X-DISCONTINUITY", StringComparison.Ordinal) < playlist.IndexOf("2.ts", StringComparison.Ordinal));
        }

        [SkippableFact]
        public async Task RecordingPlaylistShouldSplitSegmentsAtKeyFramesAndPlay()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            using (var temp = new TempDirectory())
            {
                var manager = Recordings(temp);
                var playlist = await new RecordingPlaylist(manager, TimeSpan.FromSeconds(2)).GetPlaylistAsync(
                    "cam1", Start, Start.AddSeconds(10), s => Path.GetRelativePath(temp.Path, s.Path).Replace('\\', '/'));

                // Two files of 5 s, each in ranges of at least 2 s that start at a key frame: 2 + 2 + 1 s.
                Assert.Equal(6, Regex.Matches(playlist, "#EXT-X-BYTERANGE:").Count);
                Assert.Single(Regex.Matches(playlist, "#EXT-X-DISCONTINUITY"));
                Assert.Contains("#EXT-X-PROGRAM-DATE-TIME:2026-11-07T10:00:05.000Z", playlist);
                Assert.EndsWith("#EXT-X-ENDLIST\n", playlist);

                var segment = manager.GetSegments("cam1")[0];
                var ranges = Regex.Matches(playlist, @"#EXT-X-BYTERANGE:(\d+)@(\d+)").Select(m => (Length: long.Parse(m.Groups[1].Value), Offset: long.Parse(m.Groups[2].Value))).Take(3).ToList();
                Assert.Equal(0, ranges[0].Offset);
                Assert.Equal(segment.Size, ranges.Sum(r => r.Length));

                var bytes = File.ReadAllBytes(segment.Path);
                Assert.All(ranges, r => Assert.Equal(0, ((bytes[r.Offset + 1] & 0x1F) << 8) | bytes[r.Offset + 2]));

                var path = Path.Combine(temp.Path, "recordings.m3u8");
                File.WriteAllText(path, playlist);
                Assert.Equal(string.Empty, await RealMedia.DecodeErrorsAsync(path));
                Assert.InRange((await RealMedia.ProbeAsync(path)).Duration, 9.5, 10.5);
            }
        }

        [SkippableFact]
        public async Task ClipsShouldSpanSegmentsAndStartAtAKeyFrame()
        {
            Skip.IfNot(RealMedia.IsAvailable, "ffmpeg is not installed.");

            using (var temp = new TempDirectory())
            {
                var manager = Recordings(temp);
                var path = Path.Combine(temp.Path, "clip.ts");

                // 3.5 s .. 7.2 s: starts at the key frame at 3 s, crosses into the second recording.
                using (var file = File.Create(path))
                {
                    var frames = await ClipExport.WriteAsync(manager, "cam1", Start.AddSeconds(3.5), Start.AddSeconds(7.2), file);
                    Assert.Equal((int)((7.2 - 3.0) * 25) + 1, frames);
                }

                Assert.Equal(string.Empty, await RealMedia.DecodeErrorsAsync(path));
                var probe = await RealMedia.ProbeAsync(path);
                Assert.Contains("h264", probe.Streams);
                Assert.Contains("aac,16000,1", probe.Streams);
                Assert.InRange(probe.Duration, 4.0, 4.4);

                using (var empty = new MemoryStream())
                {
                    Assert.Equal(0, await ClipExport.WriteAsync(manager, "cam1", Start.AddHours(1), Start.AddHours(2), empty));
                }
            }
        }

        #endregion
    }
}
