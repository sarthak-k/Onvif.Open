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
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Replay;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Authentication;
using Onvif.Open.Streaming.Implementation.Depacketizer;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Sdp;
using Onvif.Open.Streaming.Implementation.Transport;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// Plays the tracks of an RTSP URI chosen by an <see cref="IRtpTrackReader{TFrame}"/> (OPTIONS → DESCRIBE → SETUP → PLAY, keep-alive, TEARDOWN),
    /// receiving RTP through the selected <see cref="IRtpTransport"/> (TCP interleaved or UDP).
    /// </summary>
    internal sealed class RtspSession<TFrame> : IAsyncDisposable
        where TFrame : class
    {
        #region Constructor

        private RtspSession(Uri streamUri, NetworkCredential credential, StreamTransport transport, TimeSpan timeout, int frameBufferCapacity, DateTimeOffset? replayFrom, ReplayOptions replay, IRtpTrackReader<TFrame> trackReader)
        {
            _trackReader = trackReader;
            _replayFrom = replayFrom;
            _replay = replay;
            Transport = transport;
            StreamUri = streamUri;
            _requestUri = RtspUris.WithoutUserInfo(streamUri);
            _credential = credential;
            _timeout = timeout;
            _frames = Channel.CreateBounded<TFrame>(new BoundedChannelOptions(frameBufferCapacity)
            {
                // Live video: never block the network reader, drop the oldest frames for slow consumers.
                // Replay: every frame matters (export), so wait for the consumer; TCP flow control slows the device down.
                FullMode = replay == null ? BoundedChannelFullMode.DropOldest : BoundedChannelFullMode.Wait,
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
        private readonly Channel<TFrame> _frames;
        private readonly IRtpTrackReader<TFrame> _trackReader;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly DateTimeOffset? _replayFrom;
        private readonly ReplayOptions _replay;

        private RtspConnection _connection;
        private IRtspAuthenticator _authenticator;
        private IRtpTransport _transport;
        private string _session;
        private string _playUri;
        private TimeSpan _sessionTimeout = DefaultSessionTimeout;
        private bool _supportsGetParameter;
        private Task _readLoop = Task.CompletedTask;
        private Task _keepAlive = Task.CompletedTask;
        private int _stopped;

        #endregion

        #region Public Variables

        public Uri StreamUri { get; }
        public StreamTransport Transport { get; }
        public string SessionDescription { get; private set; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts live video, or, with <paramref name="replayFrom"/>, the replay of a recording stored on the device
        /// (ONVIF Streaming Specification: "Require: onvif-replay", absolute "clock" ranges, RTP over RTSP/TCP).
        /// </summary>
        internal static async Task<RtspSession<TFrame>> StartAsync(IRtpTrackReader<TFrame> trackReader, Uri streamUri, NetworkCredential credential, StreamTransport transport, TimeSpan timeout, int frameBufferCapacity, CancellationToken cancellationToken, DateTimeOffset? replayFrom = null, ReplayOptions replay = null)
        {
            if (replayFrom.HasValue)
            {
                replay = (replay ?? new ReplayOptions()).Validate(replayFrom.Value);

                if (transport != StreamTransport.Rtsp)
                {
                    throw new NotSupportedException("Replay is only supported with StreamTransport.Rtsp (RTP over the RTSP connection).");
                }
            }
            else
            {
                replay = null;
            }

            var stream = new RtspSession<TFrame>(streamUri, credential ?? RtspUris.CredentialFromUri(streamUri), transport, timeout, frameBufferCapacity, replayFrom, replay, trackReader ?? throw new ArgumentNullException(nameof(trackReader)));

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

        public async IAsyncEnumerable<TFrame> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var frame in _frames.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return frame;
            }
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
            _transport?.Dispose();
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
            _transport = RtpTransportFactory.Create(Transport, _connection.RemoteAddress);

            var options = await RequestAsync("OPTIONS", _requestUri, null, cancellationToken).ConfigureAwait(false);
            _supportsGetParameter = (options.GetHeader("Public") ?? string.Empty).IndexOf("GET_PARAMETER", StringComparison.OrdinalIgnoreCase) >= 0;

            var describe = await RequestAsync("DESCRIBE", _requestUri, new[] { Header("Accept", "application/sdp") }, cancellationToken).ConfigureAwait(false);
            SessionDescription = describe.Body;

            var sdp = Sdp.SessionDescription.Parse(describe.Body);
            var tracks = _trackReader.SelectTracks(sdp);

            var contentBase = describe.GetHeader("Content-Base") ?? describe.GetHeader("Content-Location") ?? _requestUri;
            _playUri = RtspUris.ResolveControl(contentBase, sdp.Control);

            // One SETUP per track; the first one creates the session, the others join it.
            for (var index = 0; index < tracks.Count; index++)
            {
                var setupUri = RtspUris.ResolveControl(contentBase, tracks[index].Control);
                var setup = await RequestAsync("SETUP", setupUri, new[] { Header("Transport", _transport.GetRequestTransport(index)) }, cancellationToken).ConfigureAwait(false);

                if (_session == null)
                {
                    ParseSession(setup.GetHeader("Session"));
                }

                _transport.Configure(index, setup.GetHeader("Transport"));
            }

            await RequestAsync("PLAY", _playUri, PlayHeaders(), cancellationToken).ConfigureAwait(false);

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

            if (_replay != null)
            {
                all.Add(Header("Require", "onvif-replay"));
            }

            return all;
        }

        private IEnumerable<KeyValuePair<string, string>> PlayHeaders()
        {
            if (_replay == null)
            {
                return new[] { Header("Range", "npt=0.000-") };
            }

            var range = "clock=" + ClockTime(_replayFrom.Value) + "-" + (_replay.To.HasValue ? ClockTime(_replay.To.Value) : string.Empty);
            var headers = new List<KeyValuePair<string, string>> { Header("Range", range) };

            if (_replay.RealTime)
            {
                if (Math.Abs(_replay.Speed - 1.0) > 1e-9)
                {
                    headers.Add(Header("Scale", _replay.Speed.ToString("0.0##", CultureInfo.InvariantCulture)));
                }
            }
            else
            {
                headers.Add(Header("Rate-Control", "no"));
            }

            return headers;
        }

        private static string ClockTime(DateTimeOffset time)
        {
            return time.UtcDateTime.ToString("yyyyMMdd'T'HHmmss.fff'Z'", CultureInfo.InvariantCulture);
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            var completed = new List<TFrame>();

            // Replay ends at the requested time (or when the device closes the connection at the end of the recording).
            using (var end = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                try
                {
                    await _transport.ReceiveAsync(_connection, async (track, data) =>
                    {
                        if (!RtpPacket.TryParse(data, out var packet))
                        {
                            return;
                        }

                        _trackReader.Process(track, packet, completed);

                        foreach (var stamped in completed)
                        {

                            if (_replay == null)
                            {
                                _frames.Writer.TryWrite(stamped);
                                continue;
                            }

                            if (_replay.To.HasValue && _trackReader.GetCaptureTime(stamped) > _replay.To.Value)
                            {
                                end.Cancel();
                                break;
                            }

                            await _frames.Writer.WriteAsync(stamped, end.Token).ConfigureAwait(false);
                        }

                        completed.Clear();
                    }, end.Token).ConfigureAwait(false);

                    _frames.Writer.TryComplete();
                }
                catch (Exception) when (end.IsCancellationRequested)
                {
                    _frames.Writer.TryComplete();
                }
                catch (IOException) when (_replay != null)
                {
                    // The device closed the connection: the recording has ended.
                    _frames.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    _frames.Writer.TryComplete(ex);
                }
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
