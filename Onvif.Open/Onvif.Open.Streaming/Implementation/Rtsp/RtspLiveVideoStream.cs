// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Replay;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// Live (or replayed) video: the video track of an <see cref="RtspSession{TFrame}"/>.
    /// </summary>
    internal sealed class RtspLiveVideoStream : ILiveVideoStream
    {
        #region Constructor

        private RtspLiveVideoStream(RtspSession<VideoFrame> session, VideoTrackReader reader)
        {
            _session = session;
            _reader = reader;
        }

        #endregion

        #region Private Variables

        private readonly RtspSession<VideoFrame> _session;
        private readonly VideoTrackReader _reader;

        #endregion

        #region Public Variables

        public Uri StreamUri => _session.StreamUri;
        public StreamTransport Transport => _session.Transport;
        public VideoCodec Codec => _reader.Codec;
        public string SessionDescription => _session.SessionDescription;

        #endregion

        #region Public Methods

        internal static async Task<ILiveVideoStream> StartAsync(Uri streamUri, NetworkCredential credential, StreamTransport transport, TimeSpan timeout, int frameBufferCapacity, CancellationToken cancellationToken, DateTimeOffset? replayFrom = null, ReplayOptions replay = null)
        {
            var reader = new VideoTrackReader();
            var session = await RtspSession<VideoFrame>.StartAsync(reader, streamUri, credential, transport, timeout, frameBufferCapacity, cancellationToken, replayFrom, replay).ConfigureAwait(false);
            return new RtspLiveVideoStream(session, reader);
        }

        public IAsyncEnumerable<VideoFrame> ReadFramesAsync(CancellationToken cancellationToken = default)
        {
            return _session.ReadFramesAsync(cancellationToken);
        }

        public async Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            await foreach (var frame in ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                await destination.WriteAsync(frame.Data, cancellationToken).ConfigureAwait(false);
            }

            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return _session.StopAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return _session.DisposeAsync();
        }

        #endregion
    }
}
