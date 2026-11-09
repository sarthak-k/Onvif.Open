// Sarthak Killedar
// 2026-11-09

using System.Collections.Generic;
using Onvif.Open.Recorder.Configuration;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public sealed class StreamSelectionTest
    {
        private static ProbeProfile Profile(string token, string encoding, int width, int height, int fps = 25) =>
            new ProbeProfile { Token = token, Encoding = encoding, Width = width, Height = height, FrameRate = fps };

        [Fact]
        public void TheSharpestStreamShouldBeMainAndTheSmallestSub()
        {
            var choice = StreamSelection.Recommend(new List<ProbeProfile>
            {
                Profile("sub", "H264", 640, 360),
                Profile("main", "H265", 3840, 2160),
                Profile("third", "H264", 1280, 720),
                Profile("jpeg", "JPEG", 320, 180)
            });

            Assert.Equal(("main", "sub"), (choice.MainProfile, choice.SubProfile));
        }

        [Fact]
        public void H264ShouldWinAtTheSameSize()
        {
            var choice = StreamSelection.Recommend(new List<ProbeProfile> { Profile("hevc", "H265", 1920, 1080), Profile("avc", "H264", 1920, 1080) });
            Assert.Equal(("avc", null), (choice.MainProfile, choice.SubProfile));
        }

        [Fact]
        public void MotionJpegShouldOnlyBeUsedAlone()
        {
            Assert.Equal("jpeg", StreamSelection.Recommend(new List<ProbeProfile> { Profile("jpeg", "JPEG", 640, 480) }).MainProfile);
            Assert.Null(StreamSelection.Recommend(new List<ProbeProfile>()).MainProfile);
        }
    }
}

namespace Onvif.Open.Test.Recording.Unit
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Onvif.Open.Core.Abstract.Interface.Camera;
    using Onvif.Open.Core.Abstract.Interface.Media;

    public sealed class CameraProbeSnapshotTest
    {
        private sealed class FakeMedia : ICameraMedia
        {
            public Func<string, CancellationToken, Task<byte[]>> Snapshot { get; set; }
            public string AskedProfile { get; private set; }

            public Task<byte[]> GetSnapshotAsync(string profileToken = null, CancellationToken cancellationToken = default)
            {
                AskedProfile = profileToken;
                return Snapshot(profileToken, cancellationToken);
            }

            public IMediaService GetMediaService() => throw new NotSupportedException();
            public Task<Core.Abstract.Interface.Streaming.ILiveVideoStream> StartLiveStreamAsync(string profileToken = null, Core.Operations.StreamTransport transport = default, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<Core.Abstract.Interface.Streaming.ILiveVideoStream> StartLiveStreamAsync(string profileToken, Core.Operations.StreamTransport transport, bool includeAudio, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<Core.Abstract.Interface.Analytics.IAnalyticsStream> StartAnalyticsStreamAsync(string profileToken = null, Core.Operations.StreamTransport transport = default, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        [Fact]
        public async Task ThePictureShouldComeAsADataUrl()
        {
            var media = new FakeMedia { Snapshot = (_, _) => Task.FromResult(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 }) };
            Assert.Equal("data:image/jpeg;base64,/9j/2Q==", await Onvif.Open.Recorder.Configuration.CameraProbe.SnapshotAsync(media, "sub", TimeSpan.FromSeconds(1), CancellationToken.None));
            Assert.Equal("sub", media.AskedProfile);
        }

        [Fact]
        public async Task CamerasWithoutPicturesShouldStillPass()
        {
            var failing = new FakeMedia { Snapshot = (_, _) => throw new InvalidOperationException("no snapshot URI") };
            Assert.Null(await Onvif.Open.Recorder.Configuration.CameraProbe.SnapshotAsync(failing, null, TimeSpan.FromSeconds(1), CancellationToken.None));

            var slow = new FakeMedia { Snapshot = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return null; } };
            Assert.Null(await Onvif.Open.Recorder.Configuration.CameraProbe.SnapshotAsync(slow, null, TimeSpan.FromMilliseconds(100), CancellationToken.None));
        }
    }
}
