// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Test.Recording.Fakes
{
    /// <summary>
    /// Live stream fed by the test: frames are pushed with <see cref="Push"/>, the stream ends with <see cref="End"/>.
    /// </summary>
    internal sealed class FakeLiveVideoStream : ILiveVideoStream
    {
        private readonly Channel<MediaFrame> _frames = Channel.CreateUnbounded<MediaFrame>();
        private int _index;

        public FakeLiveVideoStream(VideoCodec codec = VideoCodec.H264, AudioFormat audio = null)
        {
            Codec = codec;
            Audio = audio;
        }

        public AudioFormat Audio { get; }

        /// <summary>AAC (16 kHz mono) as announced by many cameras.</summary>
        public static AudioFormat Aac16k { get; } = new AudioFormat(AudioCodec.Aac, 16000, 1, new byte[] { 0x14, 0x08 });

        /// <summary>An audio frame at <paramref name="timestamp"/> (milliseconds on the video timeline).</summary>
        public static AudioFrame AudioAt(AudioFormat format, int milliseconds, int size = 50)
        {
            return new AudioFrame(format, Enumerable.Repeat((byte)0x21, size).ToArray(), (uint)milliseconds, TimeSpan.FromMilliseconds(milliseconds));
        }

        public FakeLiveVideoStream PushAudio(AudioFrame frame)
        {
            _frames.Writer.TryWrite(frame);
            return this;
        }

        public Uri StreamUri { get; } = new Uri("rtsp://192.168.1.3/stream");
        public StreamTransport Transport => StreamTransport.Rtsp;
        public VideoCodec Codec { get; }
        public string SessionDescription => string.Empty;
        public bool Disposed { get; private set; }

        public static VideoFrame Frame(int index, bool keyFrame, int size = 100)
        {
            var nal = new byte[] { 0, 0, 0, 1, keyFrame ? (byte)0x65 : (byte)0x41 }.Concat(Enumerable.Repeat((byte)index, size)).ToArray();
            return new VideoFrame(VideoCodec.H264, nal, keyFrame, (uint)(index * 3600), TimeSpan.FromMilliseconds(index * 40));
        }

        /// <summary>Pushes <paramref name="count"/> frames with a key frame every <paramref name="gop"/> frames.</summary>
        public FakeLiveVideoStream Push(int count, int gop = 5)
        {
            for (var i = 0; i < count; i++, _index++)
            {
                _frames.Writer.TryWrite(Frame(_index, _index % gop == 0));
            }

            return this;
        }

        public FakeLiveVideoStream PushFrame(VideoFrame frame)
        {
            _frames.Writer.TryWrite(frame);
            return this;
        }

        public FakeLiveVideoStream End(Exception error = null)
        {
            _frames.Writer.TryComplete(error);
            return this;
        }

        public async IAsyncEnumerable<VideoFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken))
            {
                if (frame is VideoFrame video)
                {
                    yield return video;
                }
            }
        }

        public IAsyncEnumerable<MediaFrame> ReadMediaFramesAsync(CancellationToken cancellationToken = default)
        {
            return _frames.Reader.ReadAllAsync(cancellationToken);
        }

        public async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            await foreach (var frame in ReadFramesAsync(cancellationToken))
            {
                await destination.WriteAsync(frame.Data, cancellationToken);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            _frames.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            _frames.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// Stream factory handing out scripted streams in order; once exhausted, further connects fail.
    /// </summary>
    internal sealed class FakeStreamSource
    {
        private readonly Queue<FakeLiveVideoStream> _streams = new Queue<FakeLiveVideoStream>();
        private readonly object _sync = new object();

        public int Connects { get; private set; }
        public TaskCompletionSource<bool> Exhausted { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeStreamSource Add(FakeLiveVideoStream stream)
        {
            _streams.Enqueue(stream);
            return this;
        }

        public Task<ILiveVideoStream> ConnectAsync(CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                Connects++;

                if (_streams.Count == 0)
                {
                    Exhausted.TrySetResult(true);
                    return Task.FromException<ILiveVideoStream>(new IOException("camera offline"));
                }

                return Task.FromResult<ILiveVideoStream>(_streams.Dequeue());
            }
        }
    }
}
