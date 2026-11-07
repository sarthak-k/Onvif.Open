// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Onvif.Open.Test.Camera.Fakes
{
    /// <summary>
    /// In-process RTSP server: Digest authentication, one H.264 track, RTP interleaved over TCP.
    /// After PLAY it sends <see cref="Packets"/> on channel 0 (plus an RTCP packet on channel 1).
    /// </summary>
    internal sealed class FakeRtspServer : IAsyncDisposable
    {
        public const string Realm = "IP Camera";
        public const string Nonce = "0123456789abcdef";

        public static readonly byte[] Sps = { 0x67, 0x42, 0x00, 0x1F, 0xE9, 0x01, 0x40, 0x7B, 0x20 };
        public static readonly byte[] Pps = { 0x68, 0xCE, 0x38, 0x80 };

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly string _username;
        private readonly string _password;
        private Task _serve;

        public FakeRtspServer(string username = "admin", string password = "12345")
        {
            _username = username;
            _password = password;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serve = Task.Run(ServeAsync);
        }

        public int Port { get; }
        public Uri StreamUri => new Uri($"rtsp://127.0.0.1:{Port}/stream");
        public List<byte[]> Packets { get; } = new List<byte[]>();
        public ConcurrentQueue<string> Requests { get; } = new ConcurrentQueue<string>();
        public TaskCompletionSource<bool> TeardownReceived { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Sdp =>
            "v=0\r\n" +
            "o=- 1 1 IN IP4 127.0.0.1\r\n" +
            "s=Fake\r\n" +
            "t=0 0\r\n" +
            "a=control:*\r\n" +
            "m=video 0 RTP/AVP 96\r\n" +
            "a=rtpmap:96 H264/90000\r\n" +
            $"a=fmtp:96 packetization-mode=1;profile-level-id=42001F;sprop-parameter-sets={Convert.ToBase64String(Sps)},{Convert.ToBase64String(Pps)}\r\n" +
            "a=control:trackID=1\r\n" +
            "m=audio 0 RTP/AVP 0\r\n" +
            "a=control:trackID=2\r\n";

        private async Task ServeAsync()
        {
            using (var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false))
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
            {
                while (!_stop.IsCancellationRequested)
                {
                    var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (requestLine == null)
                    {
                        return;
                    }

                    if (requestLine.Length == 0)
                    {
                        continue;
                    }

                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string line;
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false)))
                    {
                        var separator = line.IndexOf(':');
                        headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                    }

                    var parts = requestLine.Split(' ');
                    var method = parts[0];
                    var uri = parts[1];
                    Requests.Enqueue(method + " " + uri);
                    headers.TryGetValue("CSeq", out var cseq);

                    if (!IsAuthorized(method, uri, headers))
                    {
                        await WriteAsync(stream, $"RTSP/1.0 401 Unauthorized\r\nCSeq: {cseq}\r\nWWW-Authenticate: Basic realm=\"{Realm}\"\r\nWWW-Authenticate: Digest realm=\"{Realm}\", nonce=\"{Nonce}\"\r\n\r\n").ConfigureAwait(false);
                        continue;
                    }

                    switch (method)
                    {
                        case "OPTIONS":
                            await WriteAsync(stream, $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nPublic: OPTIONS, DESCRIBE, SETUP, PLAY, TEARDOWN, GET_PARAMETER\r\n\r\n").ConfigureAwait(false);
                            break;
                        case "DESCRIBE":
                            var body = Encoding.ASCII.GetBytes(Sdp);
                            await WriteAsync(stream, $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nContent-Base: {StreamUri}/\r\nContent-Type: application/sdp\r\nContent-Length: {body.Length}\r\n\r\n{Sdp}").ConfigureAwait(false);
                            break;
                        case "SETUP":
                            await WriteAsync(stream, $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nSession: 12345678;timeout=60\r\nTransport: RTP/AVP/TCP;unicast;interleaved=0-1\r\n\r\n").ConfigureAwait(false);
                            break;
                        case "PLAY":
                            await WriteAsync(stream, $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nSession: 12345678\r\n\r\n").ConfigureAwait(false);
                            await WriteInterleavedAsync(stream, 1, new byte[] { 0x80, 0xC8, 0x00, 0x06, 0, 0, 0, 0 }).ConfigureAwait(false);
                            foreach (var packet in Packets)
                            {
                                await WriteInterleavedAsync(stream, 0, packet).ConfigureAwait(false);
                            }
                            break;
                        case "TEARDOWN":
                            await WriteAsync(stream, $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\n\r\n").ConfigureAwait(false);
                            TeardownReceived.TrySetResult(true);
                            return;
                        default:
                            await WriteAsync(stream, $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\n\r\n").ConfigureAwait(false);
                            break;
                    }
                }
            }
        }

        private bool IsAuthorized(string method, string uri, IDictionary<string, string> headers)
        {
            if (!headers.TryGetValue("Authorization", out var authorization) || !authorization.StartsWith("Digest ", StringComparison.Ordinal))
            {
                return false;
            }

            var ha1 = Md5($"{_username}:{Realm}:{_password}");
            var ha2 = Md5($"{method}:{uri}");
            var expected = Md5($"{ha1}:{Nonce}:{ha2}");
            return authorization.Contains($"response=\"{expected}\"", StringComparison.Ordinal)
                   && authorization.Contains($"uri=\"{uri}\"", StringComparison.Ordinal);
        }

        private static string Md5(string value)
        {
            return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        }

        private static Task WriteAsync(Stream stream, string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            return stream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static async Task WriteInterleavedAsync(Stream stream, byte channel, byte[] data)
        {
            var header = new byte[] { (byte)'$', channel, (byte)(data.Length >> 8), (byte)data.Length };
            await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
            await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            try
            {
                await _serve.ConfigureAwait(false);
            }
            catch
            {
            }

            _stop.Dispose();
        }
    }
}
