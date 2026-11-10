// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Recorder.Tunnel;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    /// <summary>
    /// The tunnel on its own: many requests over one WebSocket, streamed responses, cancellation, slow readers.
    /// </summary>
    public sealed class TunnelConnectionTest : IAsyncDisposable
    {
        private TunnelConnection _controlPlane;
        private TunnelConnection _gateway;
        private Task _runs;

        private async Task ConnectAsync(Func<TunnelRequestHead, Stream, TunnelResponseWriter, CancellationToken, Task> gateway)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            var server = await listener.AcceptTcpClientAsync();
            listener.Stop();

            _controlPlane = new TunnelConnection(WebSocket.CreateFromStream(server.GetStream(), true, null, TimeSpan.FromSeconds(30)), null, null);
            _gateway = new TunnelConnection(WebSocket.CreateFromStream(client.GetStream(), false, null, TimeSpan.FromSeconds(30)), gateway, null);
            _runs = Task.WhenAll(_controlPlane.RunAsync(), _gateway.RunAsync());
        }

        private static TunnelRequestHead Get(string path) => new TunnelRequestHead("GET", path, "", new Dictionary<string, string>(), "org", "ada");

        public async ValueTask DisposeAsync()
        {
            if (_controlPlane != null)
            {
                await _controlPlane.DisposeAsync();
                await _gateway.DisposeAsync();
            }
        }

        [Fact]
        public async Task RequestsShouldGoThroughOneTunnelAtOnceAndStreamTheirResponses()
        {
            await ConnectAsync(async (head, body, response, ct) =>
            {
                var sent = await new StreamReader(body).ReadToEndAsync(ct);
                await response.StartAsync(200, new Dictionary<string, string> { ["Content-Type"] = "text/plain" });
                foreach (var part in new[] { $"{head.Method} {head.Path}", $" by {head.UserName}", $" got '{sent}'" })
                {
                    await response.WriteAsync(Encoding.UTF8.GetBytes(part));
                    await Task.Delay(20, ct);
                }
            });

            // Several at once, one with a body larger than a frame.
            var big = new string('x', TunnelConnection.MaxPayload * 2 + 10);
            var answers = await Task.WhenAll(Enumerable.Range(1, 10).Select(async i =>
            {
                await using (var response = await _controlPlane.SendAsync(Get($"/api/cameras/c{i}/ptz"), Encoding.UTF8.GetBytes(i == 1 ? big : $"n{i}")))
                {
                    Assert.Equal((200, "text/plain"), (response.Status, response.Headers["Content-Type"]));
                    return await new StreamReader(response.Body).ReadToEndAsync();
                }
            }));

            Assert.Equal($"GET /api/cameras/c1/ptz by ada got '{big}'", answers[0]);
            Assert.Equal("GET /api/cameras/c7/ptz by ada got 'n7'", answers[6]);
            await Eventually.True(() => _controlPlane.OpenStreams == 0 && _gateway.OpenStreams == 0, "every stream finished");
        }

        [Fact]
        public async Task ALeavingViewerShouldStopTheGatewaysWork()
        {
            var stopped = new TaskCompletionSource<bool>();
            await ConnectAsync(async (head, body, response, ct) =>
            {
                await response.StartAsync(200, new Dictionary<string, string>());
                try
                {
                    // A live stream: it goes on until the viewer leaves.
                    while (true)
                    {
                        await response.WriteAsync(new byte[1000]);
                        await Task.Delay(10, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    stopped.TrySetResult(true);
                    throw;
                }
            });

            var response = await _controlPlane.SendAsync(Get("/api/cameras/c1/live.mp4"), ReadOnlyMemory<byte>.Empty);
            var buffer = new byte[3000];
            await response.Body.ReadExactlyAsync(buffer);
            await response.DisposeAsync();

            Assert.True(await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            await Eventually.True(() => _gateway.OpenStreams == 0, "the gateway let go of the stream");
        }

        [Fact]
        public async Task ASlowViewerShouldBeDroppedWithoutHoldingUpTheOthers()
        {
            await ConnectAsync(async (head, body, response, ct) =>
            {
                await response.StartAsync(200, new Dictionary<string, string>());
                var chunks = head.Path.EndsWith("slow") ? TunnelConnection.ResponseBacklog * 2 : 3;
                for (var i = 0; i < chunks; i++)
                {
                    await response.WriteAsync(new byte[100]);
                }
            });

            // Never read: once its backlog is full, it is reset.
            var slow = await _controlPlane.SendAsync(Get("/slow"), ReadOnlyMemory<byte>.Empty);
            await using (var fast = await _controlPlane.SendAsync(Get("/fast"), ReadOnlyMemory<byte>.Empty))
            {
                var all = new MemoryStream();
                await fast.Body.CopyToAsync(all);
                Assert.Equal(300, all.Length);
            }

            await Eventually.True(() => _controlPlane.OpenStreams == 0, "the slow viewer was dropped");
            var read = new MemoryStream();
            await slow.Body.CopyToAsync(read);
            Assert.True(read.Length <= TunnelConnection.ResponseBacklog * 100, "it got at most its backlog");
        }

        [Fact]
        public async Task CancellingOneStreamMidWriteShouldNeverCloseTheTunnel()
        {
            // Regression: a viewer leaving while its frame was being sent aborted the WebSocket, and every stream with it.
            await ConnectAsync(async (head, body, response, ct) =>
            {
                await response.StartAsync(200, new Dictionary<string, string>());
                while (true)
                {
                    await response.WriteAsync(new byte[TunnelConnection.MaxPayload]);
                }
            });

            await using (var keep = await _controlPlane.SendAsync(Get("/keep"), ReadOnlyMemory<byte>.Empty))
            {
                var buffer = new byte[TunnelConnection.MaxPayload];
                for (var i = 0; i < 50; i++)
                {
                    var leaving = await _controlPlane.SendAsync(Get($"/leaving/{i}"), ReadOnlyMemory<byte>.Empty);
                    await leaving.Body.ReadExactlyAsync(buffer);
                    await leaving.DisposeAsync();
                    await keep.Body.ReadExactlyAsync(buffer);
                }

                Assert.True(_controlPlane.IsOpen && _gateway.IsOpen);
            }
        }

        [Fact]
        public async Task RequestsShouldFailWhenTheTunnelCloses()
        {
            var started = new TaskCompletionSource<bool>();
            await ConnectAsync(async (head, body, response, ct) =>
            {
                started.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, ct);
            });

            var waiting = _controlPlane.SendAsync(Get("/api/cameras/c1/snapshot"), ReadOnlyMemory<byte>.Empty);
            await started.Task;
            await _gateway.DisposeAsync();

            await Assert.ThrowsAnyAsync<IOException>(() => waiting);
            Assert.False(_controlPlane.IsOpen && _controlPlane.OpenStreams > 0);
        }
    }
}
