// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Authentication;
using Onvif.Open.Streaming.Implementation.Depacketizer;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Sdp;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// Plays the video track of an RTSP URI with RTP interleaved over the RTSP TCP connection
    /// (OPTIONS → DESCRIBE → SETUP → PLAY, keep-alive, TEARDOWN).
    /// </summary>
    internal sealed class RtspLiveVideoStream : ILiveVideoStream
    {
        #region Constructor

        private RtspLiveVideoStream(Uri streamUri, NetworkCredential credential, TimeSpan timeout, int frameBufferCapacity)
        {
            StreamUri = streamUri;
            _requestUri = WithoutUserInfo(streamUri);
            _credential = credential;
            _timeout = timeout;
            _frames = Channel.CreateBounded<VideoFrame>(new BoundedChannelOptions(frameBufferCapacity)
            {
                // Live video: never block the network reader, drop the oldest frames for slow consumers.
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,
                SingleWriter = true
            });
        }

        #endregion

        #region Private Variables

        private const int DefaultRtspPort = 554;
        private static readonly TimeSpan DefaultSessionTimeout = TimeSpan.FromSeconds(60);

        private readonly string _requestUri;
        private readonly NetworkCredential _credential;
        private readonly TimeSpan _timeout;
        private readonly Channel<VideoFrame> _frames;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

        private RtspConnection _connection;
        private IRtspAuthenticator _authenticator;
        private IRtpDepacketizer _depacketizer;
        private string _session;
        private string _playUri;
        private TimeSpan _sessionTimeout = DefaultSessionTimeout;
        private int _rtpChannel;
        private bool _supportsGetParameter;
        private Task _readLoop = Task.CompletedTask;
        private Task _keepAlive = Task.CompletedTask;
        private int _stopped;

        #endregion

        #region Public Variables

        public Uri StreamUri { get; }
        public VideoCodec Codec => _depacketizer?.Codec ?? VideoCodec.Unknown;
        public string SessionDescription { get; private set; }

        #endregion

        #region Public Methods

        internal static async Task<ILiveVideoStream> StartAsync(Uri streamUri, NetworkCredential credential, TimeSpan timeout, int frameBufferCapacity, CancellationToken cancellationToken)
        {
            var stream = new RtspLiveVideoStream(streamUri, credential ?? CredentialFromUri(streamUri), timeout, frameBufferCapacity);

            try
            {
                await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return stream;
            }
            catch
            {
                await stream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async IAsyncEnumerable<VideoFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
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

        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 1)
            {
                return;
            }

            if (_connection != null && _session != null)
            {
                try
                {
                    using (var teardown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        teardown.CancelAfter(TimeSpan.FromSeconds(2));
                        await SendWithoutResponseAsync("TEARDOWN", _playUri, teardown.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is OperationCanceledException || ex is ObjectDisposedException || ex is SocketException)
                {
                    // Best effort: the device drops the session on its own after the session timeout.
                }
            }

            _lifetime.Cancel();

            if (_connection != null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }

            await Task.WhenAll(Swallow(_readLoop), Swallow(_keepAlive)).ConfigureAwait(false);
            _frames.Writer.TryComplete();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _lifetime.Dispose();
        }

        #endregion

        #region Private Methods

        private async Task ConnectAsync(CancellationToken cancellationToken)
        {
            var port = StreamUri.IsDefaultPort || StreamUri.Port <= 0 ? DefaultRtspPort : StreamUri.Port;
            _connection = await RtspConnection.ConnectAsync(StreamUri.Host, port, _timeout, cancellationToken).ConfigureAwait(false);

            var options = await RequestAsync("OPTIONS", _requestUri, null, cancellationToken).ConfigureAwait(false);
            _supportsGetParameter = (options.GetHeader("Public") ?? string.Empty).IndexOf("GET_PARAMETER", StringComparison.OrdinalIgnoreCase) >= 0;

            var describe = await RequestAsync("DESCRIBE", _requestUri, new[] { Header("Accept", "application/sdp") }, cancellationToken).ConfigureAwait(false);
            SessionDescription = describe.Body;

            var sdp = Sdp.SessionDescription.Parse(describe.Body);
            var video = sdp.Video ?? throw new InvalidOperationException("The RTSP stream does not contain a video track.");
            _depacketizer = RtpDepacketizerFactory.Create(video);

            var contentBase = describe.GetHeader("Content-Base") ?? describe.GetHeader("Content-Location") ?? _requestUri;
            _playUri = ResolveControl(contentBase, sdp.Control);
            var setupUri = ResolveControl(contentBase, video.Control);

            var setup = await RequestAsync("SETUP", setupUri, new[] { Header("Transport", "RTP/AVP/TCP;unicast;interleaved=0-1") }, cancellationToken).ConfigureAwait(false);
            ParseSession(setup.GetHeader("Session"));
            _rtpChannel = ParseInterleavedChannel(setup.GetHeader("Transport"));

            await RequestAsync("PLAY", _playUri, new[] { Header("Range", "npt=0.000-") }, cancellationToken).ConfigureAwait(false);

            _readLoop = Task.Run(() => ReadLoopAsync(_lifetime.Token));
            _keepAlive = Task.Run(() => KeepAliveLoopAsync(_lifetime.Token));
        }

        private async Task<RtspResponse> RequestAsync(string method, string uri, IEnumerable<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
        {
            var response = await SendAndReceiveAsync(method, uri, headers, cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == 401 && _credential != null)
            {
                _authenticator = RtspAuthenticatorFactory.Create(_credential, response.GetHeaders("WWW-Authenticate"));

                if (_authenticator != null)
                {
                    response = await SendAndReceiveAsync(method, uri, headers, cancellationToken).ConfigureAwait(false);
                }
            }

            if (response.StatusCode == 401)
            {
                throw new UnauthorizedAccessException($"RTSP {method} {uri} was rejected: {response.StatusCode} {response.Reason}. Check the camera credentials.");
            }

            if (!response.IsSuccess)
            {
                throw new InvalidOperationException($"RTSP {method} {uri} failed: {response.StatusCode} {response.Reason}.");
            }

            return response;
        }

        private async Task<RtspResponse> SendAndReceiveAsync(string method, string uri, IEnumerable<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
        {
            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(_timeout);

                try
                {
                    var cseq = await _connection.SendRequestAsync(method, uri, BuildHeaders(method, uri, headers), timeoutSource.Token).ConfigureAwait(false);

                    while (true)
                    {
                        var incoming = await _connection.ReadAsync(timeoutSource.Token).ConfigureAwait(false)
                                       ?? throw new IOException($"RTSP server closed the connection during {method}.");

                        // Ignore early RTP data and requests sent by the server; wait for our response.
                        if (!incoming.IsPacket && !incoming.Response.IsRequest && (incoming.Response.CSeq == null || incoming.Response.CSeq == cseq))
                        {
                            return incoming.Response;
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Timed out waiting for the RTSP {method} response.");
                }
            }
        }

        private async Task SendWithoutResponseAsync(string method, string uri, CancellationToken cancellationToken)
        {
            await _connection.SendRequestAsync(method, uri, BuildHeaders(method, uri, null), cancellationToken).ConfigureAwait(false);
        }

        private IEnumerable<KeyValuePair<string, string>> BuildHeaders(string method, string uri, IEnumerable<KeyValuePair<string, string>> headers)
        {
            var all = new List<KeyValuePair<string, string>>(headers ?? Enumerable.Empty<KeyValuePair<string, string>>());

            if (_authenticator != null)
            {
                all.Add(Header("Authorization", _authenticator.GetAuthorization(method, uri)));
            }

            if (_session != null)
            {
                all.Add(Header("Session", _session));
            }

            return all;
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            var completed = new List<VideoFrame>();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var incoming = await _connection.ReadAsync(cancellationToken).ConfigureAwait(false);

                    if (incoming == null)
                    {
                        throw new IOException("RTSP server closed the connection.");
                    }

                    if (!incoming.IsPacket || incoming.Channel != _rtpChannel || !RtpPacket.TryParse(incoming.Data, out var packet))
                    {
                        // Keep-alive responses, RTCP and server requests need no handling.
                        continue;
                    }

                    _depacketizer.Process(packet, completed);

                    foreach (var frame in completed)
                    {
                        _frames.Writer.TryWrite(frame);
                    }

                    completed.Clear();
                }

                _frames.Writer.TryComplete();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                _frames.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                _frames.Writer.TryComplete(ex);
            }
        }

        private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(5).Ticks, _sessionTimeout.Ticks / 2));
            var method = _supportsGetParameter ? "GET_PARAMETER" : "OPTIONS";

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);

                    // The response is consumed (and ignored) by the read loop.
                    await SendWithoutResponseAsync(method, _playUri, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _frames.Writer.TryComplete(ex);
            }
        }

        private void ParseSession(string sessionHeader)
        {
            if (string.IsNullOrWhiteSpace(sessionHeader))
            {
                throw new InvalidOperationException("RTSP SETUP response did not contain a Session header.");
            }

            var parts = sessionHeader.Split(';');
            _session = parts[0].Trim();

            foreach (var part in parts.Skip(1))
            {
                var keyValue = part.Split('=');
                if (keyValue.Length == 2 &&
                    string.Equals(keyValue[0].Trim(), "timeout", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(keyValue[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                {
                    _sessionTimeout = TimeSpan.FromSeconds(seconds);
                }
            }
        }

        internal static int ParseInterleavedChannel(string transport)
        {
            foreach (var part in (transport ?? string.Empty).Split(';'))
            {
                var keyValue = part.Split('=');
                if (keyValue.Length == 2 && string.Equals(keyValue[0].Trim(), "interleaved", StringComparison.OrdinalIgnoreCase))
                {
                    var channels = keyValue[1].Split('-');
                    if (int.TryParse(channels[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel))
                    {
                        return channel;
                    }
                }
            }

            return 0;
        }

        internal static string ResolveControl(string baseUri, string control)
        {
            if (string.IsNullOrWhiteSpace(control) || control.Trim() == "*")
            {
                return baseUri;
            }

            control = control.Trim();

            if (control.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase) || control.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase))
            {
                return control;
            }

            return baseUri.EndsWith("/", StringComparison.Ordinal) ? baseUri + control : baseUri + "/" + control;
        }

        private static string WithoutUserInfo(Uri uri)
        {
            if (string.IsNullOrEmpty(uri.UserInfo))
            {
                return uri.OriginalString;
            }

            return new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
        }

        private static NetworkCredential CredentialFromUri(Uri uri)
        {
            if (string.IsNullOrEmpty(uri.UserInfo))
            {
                return null;
            }

            var parts = uri.UserInfo.Split(new[] { ':' }, 2);
            return new NetworkCredential(Uri.UnescapeDataString(parts[0]), parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty);
        }

        private static KeyValuePair<string, string> Header(string name, string value)
        {
            return new KeyValuePair<string, string>(name, value);
        }

        private static async Task Swallow(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch
            {
                // Errors are already reported through the frame channel.
            }
        }

        #endregion
    }
}
