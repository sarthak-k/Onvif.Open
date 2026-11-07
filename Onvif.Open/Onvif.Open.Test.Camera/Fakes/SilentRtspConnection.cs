// Sarthak Killedar
// 2026-11-07

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Streaming.Implementation.Rtsp;

namespace Onvif.Open.Test.Camera.Fakes
{
    /// <summary>
    /// An RTSP connection to a loopback listener that never sends anything (for transport tests).
    /// </summary>
    internal sealed class SilentRtspConnection : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private TcpClient _accepted;

        private SilentRtspConnection(TcpListener listener)
        {
            _listener = listener;
        }

        public RtspConnection Connection { get; private set; }

        public static async Task<SilentRtspConnection> CreateAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var silent = new SilentRtspConnection(listener);
            var accept = listener.AcceptTcpClientAsync();
            silent.Connection = await RtspConnection.ConnectAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, TimeSpan.FromSeconds(5), CancellationToken.None);
            silent._accepted = await accept;
            return silent;
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            _accepted?.Dispose();
            _listener.Stop();
        }
    }
}
