// Sarthak Killedar
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Core.Abstract.Interface.Streaming
{
    /// <summary>
    /// A running live video session (RTSP/RTP) delivering encoded video frames.
    /// </summary>
    public interface ILiveVideoStream : IAsyncDisposable
    {
        /// <summary>The RTSP URI being played.</summary>
        Uri StreamUri { get; }

        /// <summary>How RTP packets are received (TCP interleaved or UDP).</summary>
        StreamTransport Transport { get; }

        /// <summary>The codec of the video track.</summary>
        VideoCodec Codec { get; }

        /// <summary>Raw SDP returned by the device for this stream.</summary>
        string SessionDescription { get; }

        /// <summary>
        /// Reads frames as they arrive. H.264/H.265 frames are complete access units in Annex-B format.
        /// Enumeration ends when the stream is stopped or the connection is closed.
        /// </summary>
        IAsyncEnumerable<VideoFrame> ReadFramesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the elementary stream (e.g. a raw <c>.h264</c> file playable by ffplay/VLC) to <paramref name="destination"/>.
        /// </summary>
        Task CopyToAsync(Stream destination, CancellationToken cancellationToken = default);

        /// <summary>The audio track, when audio was requested and the stream has a supported one; otherwise null.</summary>
        AudioFormat Audio { get; }

        /// <summary>
        /// Video and audio frames in arrival order, on one timeline. Use either this or <see cref="ReadFramesAsync"/>
        /// (video only), not both: they read the same frames.
        /// </summary>
        IAsyncEnumerable<MediaFrame> ReadMediaFramesAsync(CancellationToken cancellationToken = default);

        /// <summary>Sends RTSP TEARDOWN and closes the connection.</summary>
        Task StopAsync(CancellationToken cancellationToken = default);
    }
}
