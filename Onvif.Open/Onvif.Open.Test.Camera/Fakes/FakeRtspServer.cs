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
    /// In-process RTSP server: Digest authentication, one H.264 track, RTP interleaved over TCP or UDP.
    /// After PLAY it sends <see cref="Packets"/> on channel 0 (plus an RTCP packet on channel 1), or as
    /// UDP datagrams to the client ports when the client asked for UDP in SETUP.
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
        private UdpClient _udpRtp;
        private UdpClient _udpRtcp;
        private int _clientRtpPort = -1;

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

        /// <summary>Headers of every request, as "METHOD Header: value" lines.</summary>
        public ConcurrentQueue<string> RequestHeaders { get; } = new ConcurrentQueue<string>();

        /// <summary>Closes the connection after sending the packets, like a device at the end of a recording.</summary>
        public bool CloseAfterPackets { get; set; }

        /// <summary>Adds an ONVIF metadata track (payload type 107) to the session description.</summary>
        public bool IncludeMetadataTrack { get; set; }
        /// <summary>Sends UDP packets in reverse order inside each pair, to exercise re-ordering.</summary>
        public bool ReorderUdpPackets { get; set; }

        /// <summary>Answers a UDP SETUP with TCP interleaved transport, as devices without UDP support do.</summary>
        public bool RefuseUdp { get; set; }

        public string SetupTransport { get; private set; }
        public TaskCompletionSource<bool> UdpPunchReceived { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
            "a=control:trackID=2\r\n" +
            (IncludeMetadataTrack ? "m=application 0 RTP/AVP 107\r\na=rtpmap:107 vnd.onvif.metadata/90000\r\na=control:trackID=3\r\n" : string.Empty);

        private async Task ServeAsync()
        {
            using (var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false))
            using (_stop.Token.Register(client.Dispose))
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
            {
                while (!_stop.IsCancellationRequested)
                {
                    var requestLine = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
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
                    while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false)))
                    {
                        var separator = line.IndexOf(':');
                        headers[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                    }

                    var parts = requestLine.Split(' ');
                    var method = parts[0];
                    var uri = parts[1];
                    Requests.Enqueue(method + " " + uri);
                    foreach (var header in headers)
                    {
                        RequestHeaders.Enqueue($"{method} {header.Key}: {header.Value}");
                    }
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
                            headers.TryGetValue("Transport", out var transport);
                            SetupTransport = transport;
                            await WriteAsync(stream, $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nSession: 12345678;timeout=60\r\nTransport: {Setup(transport)}\r\n\r\n").ConfigureAwait(false);
                            break;
                        case "PLAY":
                            await WriteAsync(stream, $"RTSP/1.0 200 OK\r\nCSeq: {cseq}\r\nSession: 12345678\r\n\r\n").ConfigureAwait(false);
                            if (_udpRtp != null)
                            {
                                await SendUdpAsync().ConfigureAwait(false);
                                break;
                            }

                            await WriteInterleavedAsync(stream, 1, new byte[] { 0x80, 0xC8, 0x00, 0x06, 0, 0, 0, 0 }).ConfigureAwait(false);
                            foreach (var packet in Packets)
                            {
                                await WriteInterleavedAsync(stream, 0, packet).ConfigureAwait(false);
                            }

                            if (CloseAfterPackets)
                            {
                                return;
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

        private string Setup(string transport)
        {
            var match = System.Text.RegularExpressions.Regex.Match(transport ?? string.Empty, @"client_port=(\d+)-(\d+)");

            if (!match.Success || RefuseUdp)
            {
                return "RTP/AVP/TCP;unicast;interleaved=0-1";
            }

            _clientRtpPort = int.Parse(match.Groups[1].Value);
            _udpRtp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _udpRtcp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _ = Task.Run(async () =>
            {
                try
                {
                    await _udpRtp.ReceiveAsync(_stop.Token).ConfigureAwait(false);
                    UdpPunchReceived.TrySetResult(true);
                }
                catch
                {
                }
            });

            var serverRtp = ((IPEndPoint)_udpRtp.Client.LocalEndPoint).Port;
            var serverRtcp = ((IPEndPoint)_udpRtcp.Client.LocalEndPoint).Port;
            return $"RTP/AVP;unicast;client_port={match.Groups[1].Value}-{match.Groups[2].Value};server_port={serverRtp}-{serverRtcp};source=127.0.0.1";
        }

        private async Task SendUdpAsync()
        {
            var order = new List<byte[]>(Packets);

            if (ReorderUdpPackets)
            {
                for (var i = 0; i + 1 < order.Count; i += 2)
                {
                    (order[i], order[i + 1]) = (order[i + 1], order[i]);
                }
            }

            var target = new IPEndPoint(IPAddress.Loopback, _clientRtpPort);
            foreach (var packet in order)
            {
                await _udpRtp.SendAsync(packet, target).ConfigureAwait(false);
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
            _udpRtp?.Dispose();
            _udpRtcp?.Dispose();
            try
            {
                await _serve.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch
            {
            }

            _stop.Dispose();
        }
    }
}
