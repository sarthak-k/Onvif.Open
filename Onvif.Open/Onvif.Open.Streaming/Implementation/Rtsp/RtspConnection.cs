// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// RTSP over TCP: writes requests and reads responses and interleaved ("$") RTP/RTCP packets
    /// (RFC 2326 §10.12) from the same connection.
    /// </summary>
    internal sealed class RtspConnection : IAsyncDisposable
    {
        #region Constructor

        private RtspConnection(TcpClient client, Stream stream)
        {
            _client = client;
            _stream = stream;
        }

        #endregion

        #region Private Variables

        private const string UserAgent = "Onvif.Open";
        private const int MaxHeaderLine = 16 * 1024;

        private readonly TcpClient _client;
        private readonly Stream _stream;
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly byte[] _buffer = new byte[64 * 1024];
        private int _bufferOffset;
        private int _bufferCount;
        private int _cseq;

        #endregion

        #region Public Variables

        /// <summary>The server address, with IPv4-mapped IPv6 addresses (dual-stack sockets) converted back to IPv4.</summary>
        public IPAddress RemoteAddress
        {
            get
            {
                var address = ((IPEndPoint)_client.Client.RemoteEndPoint).Address;
                return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>A connection over an already open stream (tests and benchmarks).</summary>
        internal static RtspConnection FromStream(Stream stream)
        {
            return new RtspConnection(new TcpClient(), stream);
        }

        public static async Task<RtspConnection> ConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var client = new TcpClient { NoDelay = true };

            using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutSource.CancelAfter(timeout);
                try
                {
                    // At an address the camera network rule allows (and the one checked is the one connected to).
                    client.Client.Dispose();
                    client.Client = await Onvif.Open.Core.Networking.CameraNetwork.ConnectAsync(host, port, null, timeoutSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    client.Dispose();
                    throw new TimeoutException($"Timed out connecting to RTSP server {host}:{port}.");
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            }

            return new RtspConnection(client, client.GetStream());
        }

        /// <summary>Sends a request and returns the CSeq used.</summary>
        public async Task<int> SendRequestAsync(string method, string uri, IEnumerable<KeyValuePair<string, string>> headers, CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var cseq = ++_cseq;
                var builder = new StringBuilder();
                builder.Append(method).Append(' ').Append(uri).Append(" RTSP/1.0\r\n");
                builder.Append("CSeq: ").Append(cseq.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
                builder.Append("User-Agent: ").Append(UserAgent).Append("\r\n");

                foreach (var header in headers ?? Array.Empty<KeyValuePair<string, string>>())
                {
                    if (!string.IsNullOrEmpty(header.Value))
                    {
                        builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
                    }
                }

                builder.Append("\r\n");

                var bytes = Encoding.UTF8.GetBytes(builder.ToString());
                await _stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                return cseq;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Reads the next message: either an RTSP response/request or an interleaved binary packet.
        /// Returns <c>null</c> when the server closed the connection.
        /// </summary>
        public async ValueTask<RtspIncoming> ReadAsync(CancellationToken cancellationToken)
        {
            if (!await FillAsync(1, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            if (_buffer[_bufferOffset] == (byte)'$')
            {
                if (!await FillAsync(4, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                var channel = _buffer[_bufferOffset + 1];
                var length = (_buffer[_bufferOffset + 2] << 8) | _buffer[_bufferOffset + 3];
                Consume(4);

                var data = new byte[length];
                if (!await ReadExactAsync(data, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                return RtspIncoming.FromPacket(channel, data);
            }

            var response = await ReadMessageAsync(cancellationToken).ConfigureAwait(false);
            return response == null ? null : RtspIncoming.FromResponse(response);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
            }

            _client.Dispose();
            _writeLock.Dispose();
        }

        #endregion

        #region Private Methods

        private async Task<RtspResponse> ReadMessageAsync(CancellationToken cancellationToken)
        {
            string startLine;
            do
            {
                startLine = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (startLine == null)
                {
                    return null;
                }
            }
            while (startLine.Length == 0);

            var headers = new List<KeyValuePair<string, string>>();
            string line;

            while (!string.IsNullOrEmpty(line = await ReadLineAsync(cancellationToken).ConfigureAwait(false)))
            {
                var separator = line.IndexOf(':');
                if (separator > 0)
                {
                    headers.Add(new KeyValuePair<string, string>(line.Substring(0, separator).Trim(), line.Substring(separator + 1).Trim()));
                }
            }

            if (line == null)
            {
                return null;
            }

            var body = string.Empty;
            var contentLength = headers.Find(h => string.Equals(h.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)).Value;

            if (int.TryParse(contentLength, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length) && length > 0)
            {
                var data = new byte[length];
                if (!await ReadExactAsync(data, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                body = Encoding.UTF8.GetString(data);
            }

            return new RtspResponse(startLine, headers, body);
        }

        private async ValueTask<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            var line = new StringBuilder();

            while (true)
            {
                if (!await FillAsync(1, cancellationToken).ConfigureAwait(false))
                {
                    return null;
                }

                var value = (char)_buffer[_bufferOffset];
                Consume(1);

                if (value == '\n')
                {
                    return line.ToString().TrimEnd('\r');
                }

                if (line.Length >= MaxHeaderLine)
                {
                    throw new InvalidDataException("RTSP header line too long.");
                }

                line.Append(value);
            }
        }

        private async ValueTask<bool> ReadExactAsync(byte[] destination, CancellationToken cancellationToken)
        {
            var written = 0;

            while (written < destination.Length)
            {
                if (_bufferCount == 0 && !await FillAsync(1, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }

                var count = Math.Min(_bufferCount, destination.Length - written);
                Buffer.BlockCopy(_buffer, _bufferOffset, destination, written, count);
                Consume(count);
                written += count;
            }

            return true;
        }

        private async ValueTask<bool> FillAsync(int required, CancellationToken cancellationToken)
        {
            while (_bufferCount < required)
            {
                if (_bufferOffset > 0)
                {
                    Buffer.BlockCopy(_buffer, _bufferOffset, _buffer, 0, _bufferCount);
                    _bufferOffset = 0;
                }

                var read = await _stream.ReadAsync(_buffer.AsMemory(_bufferCount, _buffer.Length - _bufferCount), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    return false;
                }

                _bufferCount += read;
            }

            return true;
        }

        private void Consume(int count)
        {
            _bufferOffset += count;
            _bufferCount -= count;

            if (_bufferCount == 0)
            {
                _bufferOffset = 0;
            }
        }

        #endregion
    }

    internal sealed class RtspIncoming
    {
        #region Constructor

        private RtspIncoming()
        {

        }

        #endregion

        #region Public Variables

        public RtspResponse Response { get; private set; }
        public int Channel { get; private set; } = -1;
        public byte[] Data { get; private set; }

        public bool IsPacket => Data != null;

        #endregion

        #region Public Methods

        public static RtspIncoming FromPacket(int channel, byte[] data)
        {
            return new RtspIncoming { Channel = channel, Data = data };
        }

        public static RtspIncoming FromResponse(RtspResponse response)
        {
            return new RtspIncoming { Response = response };
        }

        #endregion
    }
}
