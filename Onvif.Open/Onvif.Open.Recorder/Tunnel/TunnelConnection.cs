// Sarthak Killedar
// 2026-11-10

using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Onvif.Open.Recorder.Tunnel
{
    /// <summary>What a frame carries.</summary>
    public enum TunnelFrameType : byte
    {
        /// <summary>Control plane → gateway: a request's method, path and headers (JSON).</summary>
        Request = 1,
        RequestBody = 2,
        RequestEnd = 3,

        /// <summary>Gateway → control plane: the response's status and headers (JSON).</summary>
        ResponseHead = 4,
        ResponseBody = 5,
        ResponseEnd = 6,

        /// <summary>Either side: the stream is cancelled (the viewer left, or fell too far behind).</summary>
        Reset = 7
    }

    /// <summary>A request sent through the tunnel. Headers are the ones that matter to the gateway (no cookies, no auth).</summary>
    public sealed record TunnelRequestHead(string Method, string Path, string Query, Dictionary<string, string> Headers, string OrganizationId, string UserName);

    public sealed record TunnelResponseHead(int Status, Dictionary<string, string> Headers);

    /// <summary>A response coming back through the tunnel; its body streams as the gateway writes it. Dispose it to cancel.</summary>
    public sealed class TunnelResponse : IAsyncDisposable
    {
        internal TunnelResponse(TunnelResponseHead head, Stream body, Func<ValueTask> cancel)
        {
            Status = head.Status;
            Headers = head.Headers ?? new Dictionary<string, string>();
            Body = body;
            _cancel = cancel;
        }

        private readonly Func<ValueTask> _cancel;

        public int Status { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }
        public Stream Body { get; }

        public ValueTask DisposeAsync() => _cancel();
    }

    /// <summary>
    /// One gateway's tunnel: a WebSocket the gateway opened to the control plane (so nothing at the site listens on
    /// the internet), carrying many HTTP-like requests at once. Each frame is one binary message:
    /// <c>[type: 1 byte][stream: 4 bytes][payload]</c>, payloads at most <see cref="MaxPayload"/>. The control plane
    /// sends requests (<see cref="SendAsync"/>); the gateway answers them (the handler given to the constructor).
    /// A response whose reader falls <see cref="ResponseBacklog"/> chunks behind is reset, so one slow viewer never
    /// holds up the others.
    /// </summary>
    public sealed class TunnelConnection : IAsyncDisposable
    {
        #region Constructor

        /// <param name="onRequest">The gateway: answers a request (write the response with the given writer). Null on the control plane.</param>
        public TunnelConnection(WebSocket socket, Func<TunnelRequestHead, Stream, TunnelResponseWriter, CancellationToken, Task> onRequest, ILogger logger)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _onRequest = onRequest;
            _logger = logger;
        }

        #endregion

        #region Public Variables

        public const int MaxPayload = 64 * 1024;
        public const int ResponseBacklog = 256;
        private const int HeaderSize = 5;

        public static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        public bool IsOpen => _socket.State == WebSocketState.Open && !_closed.IsCancellationRequested;

        /// <summary>Requests going on right now (both directions).</summary>
        public int OpenStreams => _pending.Count + _serving.Count;

        #endregion

        #region Private Variables

        private readonly WebSocket _socket;
        private readonly Func<TunnelRequestHead, Stream, TunnelResponseWriter, CancellationToken, Task> _onRequest;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _send = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _closed = new CancellationTokenSource();
        private readonly ConcurrentDictionary<int, Pending> _pending = new ConcurrentDictionary<int, Pending>();
        private readonly ConcurrentDictionary<int, Serving> _serving = new ConcurrentDictionary<int, Serving>();
        private int _nextStream;

        /// <summary>A request this side sent, waiting for (or reading) its response.</summary>
        private sealed class Pending
        {
            public readonly TaskCompletionSource<TunnelResponseHead> Head = new TaskCompletionSource<TunnelResponseHead>(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly Channel<ReadOnlyMemory<byte>> Body = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(ResponseBacklog) { SingleReader = true, SingleWriter = true });
        }

        /// <summary>A request this side is answering.</summary>
        private sealed class Serving
        {
            public readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            public readonly Channel<ReadOnlyMemory<byte>> Body = Channel.CreateUnbounded<ReadOnlyMemory<byte>>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        }

        #endregion

        #region Public Methods

        /// <summary>Reads frames until the tunnel closes; requests and responses already going on are then cancelled.</summary>
        public async Task RunAsync(CancellationToken cancellationToken = default)
        {
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token))
            {
                var buffer = new byte[MaxPayload + HeaderSize];
                try
                {
                    while (_socket.State == WebSocketState.Open)
                    {
                        var length = 0;
                        ValueWebSocketReceiveResult received;
                        do
                        {
                            if (length == buffer.Length)
                            {
                                throw new InvalidDataException("A tunnel frame is too large.");
                            }

                            received = await _socket.ReceiveAsync(buffer.AsMemory(length), stop.Token).ConfigureAwait(false);
                            length += received.Count;
                        }
                        while (!received.EndOfMessage && received.MessageType != WebSocketMessageType.Close);

                        if (received.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        if (length < HeaderSize)
                        {
                            throw new InvalidDataException("A tunnel frame is too short.");
                        }

                        await DispatchAsync((TunnelFrameType)buffer[0], BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(1)), buffer.AsMemory(HeaderSize, length - HeaderSize).ToArray()).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested)
                {
                }
                catch (Exception ex) when (ex is WebSocketException || ex is InvalidDataException || ex is JsonException || ex is IOException)
                {
                    _logger?.LogWarning("The tunnel closed: {Error}", ex.Message);
                }
                finally
                {
                    Close();
                }
            }
        }

        /// <summary>Sends a request through the tunnel and waits for the response's status and headers.</summary>
        public async Task<TunnelResponse> SendAsync(TunnelRequestHead head, ReadOnlyMemory<byte> body, CancellationToken cancellationToken = default)
        {
            var stream = Interlocked.Increment(ref _nextStream);
            var pending = new Pending();
            if (!body.IsEmpty)
            {
                // The gateway knows from it that the request has a body.
                head = head with { Headers = new Dictionary<string, string>(head.Headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase) { ["Content-Length"] = body.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
            }

            _pending[stream] = pending;
            try
            {
                await SendFrameAsync(TunnelFrameType.Request, stream, JsonSerializer.SerializeToUtf8Bytes(head, Json), cancellationToken).ConfigureAwait(false);
                for (var offset = 0; offset < body.Length; offset += MaxPayload)
                {
                    await SendFrameAsync(TunnelFrameType.RequestBody, stream, body.Slice(offset, Math.Min(MaxPayload, body.Length - offset)), cancellationToken).ConfigureAwait(false);
                }

                await SendFrameAsync(TunnelFrameType.RequestEnd, stream, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
                using (cancellationToken.Register(() => pending.Head.TrySetCanceled()))
                {
                    var response = await pending.Head.Task.ConfigureAwait(false);
                    return new TunnelResponse(response, new ChannelStream(pending.Body.Reader), () => ResetAsync(stream));
                }
            }
            catch
            {
                await ResetAsync(stream).ConfigureAwait(false);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Close();
            if (_socket.State == WebSocketState.Open)
            {
                try
                {
                    using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException)
                {
                }
            }

            _socket.Dispose();
        }

        #endregion

        #region Internal Methods

        internal async Task SendFrameAsync(TunnelFrameType type, int stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        {
            var frame = new byte[HeaderSize + payload.Length];
            frame[0] = (byte)type;
            BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(1), stream);
            payload.CopyTo(frame.AsMemory(HeaderSize));

            // A stream's cancellation only stops it waiting for its turn: cancelling a send in flight would abort the
            // WebSocket, and with it every other stream of the tunnel. A frame that started is always sent whole.
            await _send.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _socket.SendAsync(frame, WebSocketMessageType.Binary, true, _closed.Token).ConfigureAwait(false);
            }
            finally
            {
                _send.Release();
            }
        }

        #endregion

        #region Private Methods

        private async Task DispatchAsync(TunnelFrameType type, int stream, byte[] payload)
        {
            switch (type)
            {
                case TunnelFrameType.Request when _onRequest != null:
                    var serving = new Serving();
                    _serving[stream] = serving;
                    var head = JsonSerializer.Deserialize<TunnelRequestHead>(payload, Json);
                    _ = Task.Run(() => ServeAsync(stream, head, serving));
                    break;
                case TunnelFrameType.RequestBody when _serving.TryGetValue(stream, out var serving2):
                    serving2.Body.Writer.TryWrite(payload);
                    break;
                case TunnelFrameType.RequestEnd when _serving.TryGetValue(stream, out var serving3):
                    serving3.Body.Writer.TryComplete();
                    break;
                case TunnelFrameType.ResponseHead when _pending.TryGetValue(stream, out var pending):
                    pending.Head.TrySetResult(JsonSerializer.Deserialize<TunnelResponseHead>(payload, Json));
                    break;
                case TunnelFrameType.ResponseBody when _pending.TryGetValue(stream, out var pending2):
                    if (!pending2.Body.Writer.TryWrite(payload))
                    {
                        // The reader is too far behind: drop it rather than hold up the tunnel.
                        _logger?.LogDebug("Tunnel stream {Stream} fell behind: reset", stream);
                        await ResetAsync(stream).ConfigureAwait(false);
                    }

                    break;
                case TunnelFrameType.ResponseEnd when _pending.TryRemove(stream, out var pending3):
                    pending3.Body.Writer.TryComplete();
                    break;
                case TunnelFrameType.Reset:
                    if (_serving.TryRemove(stream, out var cancelled))
                    {
                        cancelled.Cancel.Cancel();
                        cancelled.Body.Writer.TryComplete();
                    }

                    if (_pending.TryRemove(stream, out var abandoned))
                    {
                        abandoned.Head.TrySetException(new IOException("The gateway cancelled the request."));
                        abandoned.Body.Writer.TryComplete(new IOException("The gateway cancelled the response."));
                    }

                    break;
            }
        }

        private async Task ServeAsync(int stream, TunnelRequestHead head, Serving serving)
        {
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(serving.Cancel.Token, _closed.Token))
            {
                var writer = new TunnelResponseWriter(this, stream, stop.Token);
                try
                {
                    await _onRequest(head, new ChannelStream(serving.Body.Reader), writer, stop.Token).ConfigureAwait(false);
                    await writer.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (stop.IsCancellationRequested || ex is WebSocketException || ex is IOException)
                {
                    // The control plane cancelled it, or the tunnel closed.
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "A tunnelled request failed: {Method} {Path}", head.Method, head.Path);
                    await writer.FailAsync().ConfigureAwait(false);
                }
                finally
                {
                    _serving.TryRemove(stream, out _);
                }
            }
        }

        private async ValueTask ResetAsync(int stream)
        {
            if (_pending.TryRemove(stream, out var pending))
            {
                pending.Body.Writer.TryComplete();
                pending.Head.TrySetCanceled();
                if (IsOpen)
                {
                    try
                    {
                        await SendFrameAsync(TunnelFrameType.Reset, stream, ReadOnlyMemory<byte>.Empty, _closed.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is ObjectDisposedException)
                    {
                    }
                }
            }
        }

        private void Close()
        {
            if (_closed.IsCancellationRequested)
            {
                return;
            }

            _closed.Cancel();
            foreach (var pending in _pending.Values)
            {
                pending.Head.TrySetException(new IOException("The tunnel to the gateway closed."));
                pending.Body.Writer.TryComplete(new IOException("The tunnel to the gateway closed."));
            }

            _pending.Clear();
            foreach (var serving in _serving.Values)
            {
                serving.Cancel.Cancel();
            }
        }

        #endregion
    }

    /// <summary>Writes one response through the tunnel: the head once, then body chunks, then the end.</summary>
    public sealed class TunnelResponseWriter
    {
        internal TunnelResponseWriter(TunnelConnection connection, int stream, CancellationToken cancellationToken)
        {
            _connection = connection;
            _stream = stream;
            _cancellationToken = cancellationToken;
        }

        private readonly TunnelConnection _connection;
        private readonly int _stream;
        private readonly CancellationToken _cancellationToken;
        private bool _started;
        private bool _completed;

        public bool HasStarted => _started;

        public async Task StartAsync(int status, Dictionary<string, string> headers)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            await _connection.SendFrameAsync(TunnelFrameType.ResponseHead, _stream, JsonSerializer.SerializeToUtf8Bytes(new TunnelResponseHead(status, headers), TunnelConnection.Json), _cancellationToken).ConfigureAwait(false);
        }

        public async Task WriteAsync(ReadOnlyMemory<byte> data)
        {
            for (var offset = 0; offset < data.Length; offset += TunnelConnection.MaxPayload)
            {
                await _connection.SendFrameAsync(TunnelFrameType.ResponseBody, _stream, data.Slice(offset, Math.Min(TunnelConnection.MaxPayload, data.Length - offset)), _cancellationToken).ConfigureAwait(false);
            }
        }

        internal async Task CompleteAsync()
        {
            if (_completed)
            {
                return;
            }

            await StartAsync(204, new Dictionary<string, string>()).ConfigureAwait(false);
            _completed = true;
            await _connection.SendFrameAsync(TunnelFrameType.ResponseEnd, _stream, ReadOnlyMemory<byte>.Empty, _cancellationToken).ConfigureAwait(false);
        }

        internal async Task FailAsync()
        {
            try
            {
                if (!_started)
                {
                    await StartAsync(502, new Dictionary<string, string>()).ConfigureAwait(false);
                    await CompleteAsync().ConfigureAwait(false);
                }
                else
                {
                    await _connection.SendFrameAsync(TunnelFrameType.Reset, _stream, ReadOnlyMemory<byte>.Empty, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is WebSocketException || ex is OperationCanceledException || ex is IOException)
            {
            }
        }
    }

    /// <summary>A read-only stream over chunks from a channel.</summary>
    internal sealed class ChannelStream : Stream
    {
        public ChannelStream(ChannelReader<ReadOnlyMemory<byte>> reader)
        {
            _reader = reader;
        }

        private readonly ChannelReader<ReadOnlyMemory<byte>> _reader;
        private ReadOnlyMemory<byte> _current;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_current.IsEmpty)
            {
                if (!await _reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    return 0;
                }

                _reader.TryRead(out _current);
            }

            var count = Math.Min(buffer.Length, _current.Length);
            _current.Slice(0, count).CopyTo(buffer);
            _current = _current.Slice(count);
            return count;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
