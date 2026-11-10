// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Recorder.Security;

namespace Onvif.Open.Recorder.Tunnel
{
    /// <summary>
    /// The gateway's side of the relay: runs a request that came through the tunnel through this recorder's own
    /// pipeline (routing, authorization, endpoints and their filters), in process, and streams the response back. The
    /// request acts as the control plane, in the organization of the camera it is about: the control plane already
    /// checked the viewer may see it.
    /// </summary>
    public sealed class TunnelExecutor
    {
        public TunnelExecutor(IServiceScopeFactory scopes)
        {
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        }

        private readonly IServiceScopeFactory _scopes;

        /// <summary>The application's pipeline (set at startup, see Program).</summary>
        public RequestDelegate Pipeline { get; set; }

        /// <summary>Who a tunnelled request acts as: the control plane, for one organization.</summary>
        public static ClaimsPrincipal ControlPlane(string organizationId, string userName)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, "control-plane"),
                new Claim(ClaimTypes.Name, string.IsNullOrWhiteSpace(userName) ? "control plane" : $"{userName} (control plane)"),
                new Claim(ClaimTypes.Role, Roles.Owner),
                new Claim(RecorderClaims.Tunnel, "1")
            };
            if (!string.IsNullOrEmpty(organizationId))
            {
                claims.Add(new Claim(RecorderClaims.Organization, organizationId));
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Tunnel", ClaimTypes.Name, ClaimTypes.Role));
        }

        public static bool IsControlPlane(ClaimsPrincipal user) => user?.HasClaim(c => c.Type == RecorderClaims.Tunnel) == true;

        public async Task ExecuteAsync(TunnelRequestHead head, Stream body, TunnelResponseWriter writer, CancellationToken cancellationToken)
        {
            var pipeline = Pipeline ?? throw new InvalidOperationException("The tunnel is not ready: the application has not started.");
            await using (var scope = _scopes.CreateAsyncScope())
            {
                var headers = new HeaderDictionary();
                foreach (var (name, value) in head.Headers ?? new Dictionary<string, string>())
                {
                    headers[name] = value;
                }

                headers["Host"] = "gateway";
                var request = new HttpRequestFeature
                {
                    Protocol = "HTTP/1.1",
                    Scheme = "https",
                    Method = head.Method,
                    PathBase = "",
                    Path = head.Path,
                    QueryString = head.Query ?? "",
                    RawTarget = head.Path + head.Query,
                    Headers = headers,
                    Body = body
                };

                var response = new TunnelResponseFeature(writer);
                var features = new FeatureCollection();
                features.Set<IHttpRequestFeature>(request);
                features.Set<IHttpResponseFeature>(response);
                features.Set<IHttpResponseBodyFeature>(response);
                features.Set<IHttpRequestLifetimeFeature>(new HttpRequestLifetimeFeature { RequestAborted = cancellationToken });
                features.Set<IServiceProvidersFeature>(new ServiceProvidersFeature { RequestServices = scope.ServiceProvider });
                features.Set<IHttpConnectionFeature>(new HttpConnectionFeature { ConnectionId = "tunnel" });
                features.Set<IHttpRequestBodyDetectionFeature>(new BodyDetection(headers.ContentLength > 0));

                var context = new DefaultHttpContext(features) { User = ControlPlane(head.OrganizationId, head.UserName) };
                await pipeline(context).ConfigureAwait(false);
                await response.CompleteAsync().ConfigureAwait(false);
            }
        }

        /// <summary>Whether the request has a body: without it, endpoints would read none (and bind null).</summary>
        private sealed class BodyDetection : IHttpRequestBodyDetectionFeature
        {
            public BodyDetection(bool canHaveBody) => CanHaveBody = canHaveBody;

            public bool CanHaveBody { get; }
        }

        /// <summary>A response written to the tunnel: the head on the first write (or when it completes), then the body.</summary>
        private sealed class TunnelResponseFeature : IHttpResponseFeature, IHttpResponseBodyFeature
        {
            public TunnelResponseFeature(TunnelResponseWriter writer)
            {
                _writer = writer;
                _stream = new BodyStream(this);
                Writer = PipeWriter.Create(_stream);
            }

            private readonly TunnelResponseWriter _writer;
            private readonly BodyStream _stream;
            private readonly List<(Func<object, Task> Callback, object State)> _starting = new();
            private readonly List<(Func<object, Task> Callback, object State)> _completed = new();

            public int StatusCode { get; set; } = 200;
            public string ReasonPhrase { get; set; }
            public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
            public Stream Body { get => _stream; set => throw new NotSupportedException(); }
            public bool HasStarted { get; private set; }
            public Stream Stream => _stream;
            public PipeWriter Writer { get; }

            public void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));
            public void OnCompleted(Func<object, Task> callback, object state) => _completed.Add((callback, state));
            public void DisableBuffering() { }

            public async Task StartAsync(CancellationToken cancellationToken = default)
            {
                if (HasStarted)
                {
                    return;
                }

                for (var i = _starting.Count - 1; i >= 0; i--)
                {
                    await _starting[i].Callback(_starting[i].State).ConfigureAwait(false);
                }

                HasStarted = true;
                await _writer.StartAsync(StatusCode, Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase)).ConfigureAwait(false);
            }

            public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) =>
                SendFileFallback.SendFileAsync(_stream, path, offset, count, cancellationToken);

            public async Task CompleteAsync()
            {
                await Writer.FlushAsync().ConfigureAwait(false);
                await StartAsync().ConfigureAwait(false);
                foreach (var (callback, state) in _completed)
                {
                    await callback(state).ConfigureAwait(false);
                }
            }

            public async Task WriteAsync(ReadOnlyMemory<byte> data)
            {
                await StartAsync().ConfigureAwait(false);
                if (!data.IsEmpty)
                {
                    await _writer.WriteAsync(data).ConfigureAwait(false);
                }
            }

            private sealed class BodyStream : Stream
            {
                public BodyStream(TunnelResponseFeature owner) => _owner = owner;

                private readonly TunnelResponseFeature _owner;

                public override bool CanRead => false;
                public override bool CanSeek => false;
                public override bool CanWrite => true;
                public override long Length => throw new NotSupportedException();
                public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

                public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => new ValueTask(_owner.WriteAsync(buffer));
                public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _owner.WriteAsync(buffer.AsMemory(offset, count));
                public override void Write(byte[] buffer, int offset, int count) => _owner.WriteAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();
                public override Task FlushAsync(CancellationToken cancellationToken) => _owner.StartAsync(cancellationToken);
                public override void Flush() => FlushAsync().GetAwaiter().GetResult();
                public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
                public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                public override void SetLength(long value) => throw new NotSupportedException();
            }
        }
    }
}
