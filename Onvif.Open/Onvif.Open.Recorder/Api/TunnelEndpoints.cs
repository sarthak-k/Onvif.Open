// Sarthak Killedar
// 2026-11-10

using System.Collections.Generic;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Recorder.Tunnel;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// The tunnel: a paired gateway opens <c>/api/gateways/tunnel</c> (a WebSocket, with its token) and keeps it open;
    /// the control plane sends it requests through it, among them <c>PUT /api/gateway/cameras</c>, the cameras it
    /// records, which only the control plane may call.
    /// </summary>
    internal static class TunnelEndpoints
    {
        public static IEndpointRouteBuilder MapTunnelApi(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/gateways/tunnel", async (HttpContext http, GatewayService gateways, TunnelRegistry tunnels, ILogger<TunnelConnection> logger, CancellationToken ct) =>
            {
                var gateway = await gateways.AuthenticateAsync(http.Request.Headers[GatewayEndpoints.GatewayTokenHeader].ToString(), ct);
                if (gateway == null)
                {
                    return Results.Unauthorized();
                }

                if (!http.WebSockets.IsWebSocketRequest)
                {
                    return Results.BadRequest(new { errors = new[] { "The tunnel is a WebSocket." } });
                }

                var socket = await http.WebSockets.AcceptWebSocketAsync();
                await using (var tunnel = new TunnelConnection(socket, null, logger))
                {
                    var replaced = tunnels.Add(gateway.Id, tunnel);
                    if (replaced != null)
                    {
                        await replaced.DisposeAsync();
                    }

                    logger.LogInformation("Gateway {GatewayId} ({Name}) connected its tunnel", gateway.Id, gateway.Name);
                    try
                    {
                        await tunnel.RunAsync(ct);
                    }
                    finally
                    {
                        tunnels.Remove(gateway.Id, tunnel);
                        logger.LogInformation("Gateway {GatewayId} ({Name}) closed its tunnel", gateway.Id, gateway.Name);
                    }
                }

                return Results.Empty;
            }).AllowAnonymous();

            app.MapPut(RemoteCameras.SyncPath, async ([Microsoft.AspNetCore.Mvc.FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] List<AssignedCamera> cameras, HttpContext http, CancellationToken ct) =>
            {
                var assigned = http.RequestServices.GetService<AssignedCameras>();
                if (assigned == null)
                {
                    return Results.NotFound();
                }

                // A missing list is a mistake, never "record nothing": that would stop every camera.
                if (cameras == null)
                {
                    return Results.BadRequest(new { errors = new[] { "The list of cameras is missing." } });
                }

                await assigned.ApplyAsync(cameras, ct);
                return Results.NoContent();
            }).RequireAuthorization(Security.Policies.ControlPlane);

            return app;
        }
    }
}
