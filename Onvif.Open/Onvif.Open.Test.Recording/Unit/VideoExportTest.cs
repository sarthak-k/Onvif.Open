// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Export;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class VideoExportTest
    {
        [Fact]
        public async Task ShouldWriteFromTheFirstKeyFrameUntilTheStreamEnds()
        {
            var stream = new FakeLiveVideoStream()
                .PushFrame(FakeLiveVideoStream.Frame(0, false))
                .PushFrame(FakeLiveVideoStream.Frame(1, false))
                .Push(10, gop: 5)
                .End();

            using (var output = new NoSyncWriteStream())
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var frames = await VideoExport.WriteAsync(stream, output, cancellationToken: timeout.Token);

                Assert.Equal(10, frames);
                Assert.True(output.Length > 0 && output.Length % 188 == 0, $"{output.Length} bytes");
                Assert.Equal(0x47, output.ToArray()[0]);
                Assert.Equal(".ts", VideoExport.GetFileExtension(stream));
            }
        }

        [Fact]
        public async Task UnsupportedCodecsShouldBeRejected()
        {
            var stream = new FakeLiveVideoStream(VideoCodec.Unknown).End();
            await Assert.ThrowsAsync<NotSupportedException>(() => VideoExport.WriteAsync(stream, new MemoryStream(), SegmentFormats.MpegTs));
        }

        /// <summary>Like an ASP.NET Core response body: synchronous writes are not allowed.</summary>
        private sealed class NoSyncWriteStream : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous writes are disallowed.");
            public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Synchronous writes are disallowed.");
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                base.Write(buffer, offset, count);
                return Task.CompletedTask;
            }
        }
    }
}
