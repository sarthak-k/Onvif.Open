// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Recording.Implementation.Format;
using Onvif.Open.Streaming.Implementation.Depacketizer;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Rtsp;

namespace Onvif.Open.Benchmarks
{
    /// <summary>One second of one camera (25 frames, about 510 KB) through each stage of recording.</summary>
    [MemoryDiagnoser]
    public class RecordingPathBenchmarks
    {
        private List<byte[]> _rtp;
        private RtpPacket[] _packets;
        private List<VideoFrame> _frames;
        private byte[] _interleaved;
        private AudioFrame[] _audio;

        [GlobalSetup]
        public void Setup()
        {
            _rtp = Media.RtpGop();
            _packets = _rtp.ConvertAll(p => RtpPacket.TryParse(p, out var packet) ? packet : throw new InvalidDataException()).ToArray();

            _frames = new List<VideoFrame>();
            var depacketizer = new H264Depacketizer(90000, new[] { Media.Sps, Media.Pps });
            foreach (var packet in _packets)
            {
                depacketizer.Process(packet, _frames);
            }

            // RTP over RTSP (interleaved): '$', channel, length, packet.
            using (var stream = new MemoryStream())
            {
                foreach (var packet in _rtp)
                {
                    stream.WriteByte((byte)'$');
                    stream.WriteByte(0);
                    stream.WriteByte((byte)(packet.Length >> 8));
                    stream.WriteByte((byte)packet.Length);
                    stream.Write(packet);
                }

                _interleaved = stream.ToArray();
            }

            // AAC-LC 16 kHz mono: about 15.6 frames a second of 200 bytes.
            _audio = new AudioFrame[16];
            for (var i = 0; i < _audio.Length; i++)
            {
                _audio[i] = new AudioFrame(Audio, new byte[200], (uint)(i * 1024), TimeSpan.FromSeconds(i * 1024 / 16000.0));
            }
        }

        private static readonly AudioFormat Audio = new AudioFormat(AudioCodec.Aac, 16000, 1, new byte[] { 0x14, 0x08 });

        [Benchmark(Description = "RTSP: read interleaved RTP")]
        public async Task<int> ReadRtsp()
        {
            var count = 0;
            await using (var connection = RtspConnection.FromStream(new MemoryStream(_interleaved, false)))
            {
                while (await connection.ReadAsync(CancellationToken.None) is { } incoming)
                {
                    count += incoming.Data.Length;
                }
            }

            return count;
        }

        [Benchmark(Description = "RTP: depacketize H.264")]
        public int Depacketize()
        {
            var frames = new List<VideoFrame>(Media.Fps);
            var depacketizer = new H264Depacketizer(90000, new[] { Media.Sps, Media.Pps });

            foreach (var packet in _packets)
            {
                depacketizer.Process(packet, frames);
            }

            return frames.Count;
        }

        [Benchmark(Description = "MPEG-TS: mux video + audio")]
        public long Mux()
        {
            var output = new CountingStream();
            using (var muxer = new MpegTsMuxer(output, VideoCodec.H264, Audio))
            {
                var audio = 0;
                foreach (var frame in _frames)
                {
                    muxer.Write(frame);
                    while (audio < _audio.Length && _audio[audio].Timestamp <= frame.Timestamp)
                    {
                        muxer.Write(_audio[audio++]);
                    }
                }
            }

            return output.Length;
        }

        /// <summary>A sink that only counts, so the benchmark measures the muxer and not a disk.</summary>
        private sealed class CountingStream : Stream
        {
            private long _length;

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _length;
            public override long Position { get => _length; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => _length += count;
            public override void Write(ReadOnlySpan<byte> buffer) => _length += buffer.Length;
        }
    }
}
