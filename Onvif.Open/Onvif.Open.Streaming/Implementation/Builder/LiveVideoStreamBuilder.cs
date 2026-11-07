// Sarthak Killedar
// 2026-11-07

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Streaming.Implementation.Rtsp;
using Onvif.Open.Streaming.Interface;

namespace Onvif.Open.Streaming.Implementation.Builder
{
    /// <summary>
    /// Builds and starts a live RTSP video stream.
    /// <code>
    /// await using var stream = await LiveVideoStreamBuilder.GetBuilder()
    ///     .SetStreamUri(new Uri("rtsp://192.168.1.10/stream1"))
    ///     .SetCredentials("admin", "12345")
    ///     .BuildAsync();
    /// </code>
    /// </summary>
    public sealed class LiveVideoStreamBuilder : ILiveVideoStreamBuilder
    {
        #region Constructor

        private LiveVideoStreamBuilder()
        {

        }

        #endregion

        #region Private Variables

        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
        private const int DefaultFrameBufferCapacity = 256;

        private Uri _streamUri;
        private NetworkCredential _credential;
        private TimeSpan _timeout = DefaultTimeout;
        private int _frameBufferCapacity = DefaultFrameBufferCapacity;

        #endregion

        #region Public Methods

        /// <summary>Connects to the device and starts playing. The returned stream must be disposed.</summary>
        public Task<ILiveVideoStream> BuildAsync(CancellationToken cancellationToken = default)
        {
            if (_streamUri == null)
            {
                return Task.FromException<ILiveVideoStream>(new InvalidOperationException("Live Video Stream Builder must have a stream URL to start streaming."));
            }

            return RtspLiveVideoStream.StartAsync(_streamUri, _credential, _timeout, _frameBufferCapacity, cancellationToken);
        }

        public LiveVideoStreamBuilder SetStreamUri(Uri streamUri)
        {
            if (streamUri == null)
            {
                throw new ArgumentNullException(nameof(streamUri));
            }

            if (!streamUri.IsAbsoluteUri || !string.Equals(streamUri.Scheme, "rtsp", StringComparison.OrdinalIgnoreCase))
            {
                throw new UriFormatException("Stream URL must be an absolute rtsp:// URL.");
            }

            _streamUri = streamUri;
            return this;
        }

        /// <summary>Uses the camera's ONVIF credentials for RTSP authentication.</summary>
        public LiveVideoStreamBuilder SetCamera(ICameraInformation camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            _credential = string.IsNullOrEmpty(camera.Username) ? null : new NetworkCredential(camera.Username, camera.Password ?? string.Empty);
            return this;
        }

        public LiveVideoStreamBuilder SetCredentials(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentNullException(nameof(username));
            }

            _credential = new NetworkCredential(username, password ?? string.Empty);
            return this;
        }

        /// <summary>Timeout for connecting and for each RTSP request. Default: 10 seconds.</summary>
        public LiveVideoStreamBuilder SetTimeout(TimeSpan timeout)
        {
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            _timeout = timeout;
            return this;
        }

        /// <summary>Frames buffered for slow consumers before the oldest are dropped. Default: 256.</summary>
        public LiveVideoStreamBuilder SetFrameBufferCapacity(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _frameBufferCapacity = capacity;
            return this;
        }

        public static LiveVideoStreamBuilder GetBuilder()
        {
            return new LiveVideoStreamBuilder();
        }

        #endregion
    }
}
