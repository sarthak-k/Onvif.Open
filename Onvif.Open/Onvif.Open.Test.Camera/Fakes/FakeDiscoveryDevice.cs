// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Onvif.Open.Test.Camera.Fakes
{
    /// <summary>
    /// A device answering WS-Discovery probes sent to it (unicast) on the loopback interface.
    /// </summary>
    internal sealed class FakeDiscoveryDevice : IDisposable
    {
        private readonly UdpClient _socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        public FakeDiscoveryDevice(string uuid, string name, string xaddrs)
        {
            Uuid = uuid;
            Name = name;
            XAddrs = xaddrs;
            Port = ((IPEndPoint)_socket.Client.LocalEndPoint).Port;
            _ = Task.Run(ServeAsync);
        }

        public string Uuid { get; }
        public string Name { get; }
        public string XAddrs { get; }
        public int Port { get; }

        public TimeSpan Delay { get; set; }
        public bool OmitRelatesTo { get; set; }
        public bool WrongRelatesTo { get; set; }
        public ConcurrentQueue<string> Probes { get; } = new ConcurrentQueue<string>();

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                UdpReceiveResult request;
                try
                {
                    request = await _socket.ReceiveAsync(_stop.Token);
                }
                catch (Exception)
                {
                    return;
                }

                var probe = Encoding.UTF8.GetString(request.Buffer);
                Probes.Enqueue(probe);
                var messageId = Regex.Match(probe, "<a:MessageID>([^<]+)</a:MessageID>").Groups[1].Value;
                var relatesTo = WrongRelatesTo ? "urn:uuid:" + Guid.NewGuid() : messageId;

                var response =
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                    "<SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://www.w3.org/2003/05/soap-envelope\" xmlns:wsa=\"http://schemas.xmlsoap.org/ws/2004/08/addressing\" xmlns:d=\"http://schemas.xmlsoap.org/ws/2005/04/discovery\" xmlns:dn=\"http://www.onvif.org/ver10/network/wsdl\">" +
                    "<SOAP-ENV:Header>" +
                    $"<wsa:MessageID>urn:uuid:{Guid.NewGuid()}</wsa:MessageID>" +
                    (OmitRelatesTo ? string.Empty : $"<wsa:RelatesTo>{relatesTo}</wsa:RelatesTo>") +
                    "<wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches</wsa:Action>" +
                    "</SOAP-ENV:Header><SOAP-ENV:Body><d:ProbeMatches><d:ProbeMatch>" +
                    $"<wsa:EndpointReference><wsa:Address>urn:uuid:{Uuid}</wsa:Address></wsa:EndpointReference>" +
                    "<d:Types>dn:NetworkVideoTransmitter</d:Types>" +
                    $"<d:Scopes>onvif://www.onvif.org/name/{Uri.EscapeDataString(Name)} onvif://www.onvif.org/hardware/DS-2CD2143 onvif://www.onvif.org/Profile/Streaming</d:Scopes>" +
                    $"<d:XAddrs>{XAddrs}</d:XAddrs><d:MetadataVersion>1</d:MetadataVersion>" +
                    "</d:ProbeMatch></d:ProbeMatches></SOAP-ENV:Body></SOAP-ENV:Envelope>";

                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay);
                }

                var bytes = Encoding.UTF8.GetBytes(response);
                await _socket.SendAsync(bytes, bytes.Length, request.RemoteEndPoint);
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _socket.Dispose();
        }
    }
}
