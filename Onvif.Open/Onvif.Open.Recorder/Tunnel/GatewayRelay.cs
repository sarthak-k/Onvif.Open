// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Tunnel
{
    /// <summary>
    /// The media relay: a request about a camera that another gateway records (<c>/api/cameras/{id}/…</c>: live
    /// video, recordings, PTZ, imaging, outputs, recordings on the device) goes through that gateway's tunnel, and
    /// its response streams back. It runs once the request is authorized and the camera is one the user may see, so
    /// viewers never reach the site's network; what it does is audited here, as for local cameras.
    /// </summary>
    public static class GatewayRelay
    {
        #region Private Variables

        /// <summary>Request headers the gateway needs (never the viewer's credentials or cookies).</summary>
        private static readonly string[] RequestHeaders = { "Accept", "Content-Type", "Range", "If-None-Match" };

        /// <summary>Response headers passed back to the viewer.</summary>
        private static readonly string[] ResponseHeaders =
        {
            "Content-Type", "Content-Length", "Content-Range", "Accept-Ranges", "Content-Disposition", "Cache-Control", "ETag",
            Api.PlaybackEndpoints.CodecHeader, Api.PlaybackEndpoints.StreamHeader
        };

        #endregion

        #region Public Methods

        public static WebApplication UseGatewayRelay(this WebApplication app)
        {
            app.Use(async (context, next) =>
            {
                var camera = RemoteCameraOf(context);
                if (camera != null)
                {
                    await RelayAsync(context, camera.GatewayId, camera.OrganizationId, camera);
                    return;
                }

                // Testing a camera another gateway will record: from that gateway's network, not this one's.
                var gateway = await RemoteTestGatewayOfAsync(context);
                if (gateway != null)
                {
                    await RelayAsync(context, gateway, context.User.OrganizationId(), null);
                    return;
                }

                await next();
            });

            return app;
        }

        /// <summary>Tunnel requests about a camera: the camera's page (status) is the control plane's own; anything below it is the gateway's.</summary>
        public static bool IsRelayed(PathString path, out string cameraId)
        {
            cameraId = null;
            if (!path.StartsWithSegments("/api/cameras", StringComparison.OrdinalIgnoreCase, out var rest) || !rest.HasValue)
            {
                return false;
            }

            var parts = rest.Value.Trim('/').Split('/', 2);
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                return false;
            }

            cameraId = Uri.UnescapeDataString(parts[0]);
            return true;
        }

        #endregion

        #region Private Methods

        private static CameraEntry RemoteCameraOf(HttpContext context)
        {
            // A request from the control plane is for this gateway: it is never relayed again.
            if (context.GetEndpoint() == null || context.User.Identity?.IsAuthenticated != true || TunnelExecutor.IsControlPlane(context.User) || !IsRelayed(context.Request.Path, out var cameraId))
            {
                return null;
            }

            var services = context.RequestServices;
            var camera = services.GetRequiredService<CameraDirectory>().Find(cameraId);
            var local = services.GetRequiredService<GatewayService>().Local?.Id;
            var recordedHere = camera != null && services.GetRequiredService<Onvif.Open.Core.Abstract.Interface.Recording.IRecordingManager>().GetStatus().Any(s => string.Equals(s.CameraId, camera.Id, StringComparison.OrdinalIgnoreCase));
            return camera?.GatewayId != null && !recordedHere && !string.Equals(camera.GatewayId, local, StringComparison.OrdinalIgnoreCase) ? camera : null;
        }

        /// <summary>POST /api/config/cameras/test?gatewayId=…: the organization's gateway to test from, when it is not this recorder.</summary>
        private static async Task<string> RemoteTestGatewayOfAsync(HttpContext context)
        {
            if (context.GetEndpoint() == null || TunnelExecutor.IsControlPlane(context.User) || !HttpMethods.IsPost(context.Request.Method)
                || !context.Request.Path.Equals("/api/config/cameras/test", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var requested = context.Request.Query["gatewayId"].ToString();
            var gateways = context.RequestServices.GetRequiredService<GatewayService>();
            if (string.IsNullOrEmpty(requested) || string.Equals(requested, gateways.Local?.Id, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            // Not one of the organization's gateways: tested here, as if none was named.
            return await gateways.FindAsync(context.User.OrganizationId(), requested, context.RequestAborted) == null ? null : requested;
        }

        private static async Task RelayAsync(HttpContext context, string gatewayId, string organizationId, CameraEntry camera)
        {
            var tunnel = context.RequestServices.GetRequiredService<TunnelRegistry>().Find(gatewayId);
            if (tunnel == null)
            {
                await Problem(context, StatusCodes.Status503ServiceUnavailable, camera == null ? "That gateway is offline: it cannot test the camera." : "The gateway that records this camera is offline.");
                return;
            }

            byte[] body = Array.Empty<byte>();
            if (context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count > 0)
            {
                using (var buffer = new MemoryStream())
                {
                    await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
                    body = buffer.ToArray();
                }
            }

            var headers = RequestHeaders.Where(h => context.Request.Headers.ContainsKey(h)).ToDictionary(h => h, h => context.Request.Headers[h].ToString());
            var head = new TunnelRequestHead(context.Request.Method, context.Request.Path, context.Request.QueryString.Value ?? "", headers, organizationId, context.User.Identity?.Name);

            TunnelResponse response;
            try
            {
                response = await tunnel.SendAsync(head, body, context.RequestAborted);
            }
            catch (Exception ex) when (ex is IOException || ex is WebSocketException)
            {
                await Problem(context, StatusCodes.Status503ServiceUnavailable, "The gateway that records this camera is offline.");
                return;
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                return;
            }

            await using (response)
            {
                context.Response.StatusCode = response.Status;
                foreach (var name in ResponseHeaders)
                {
                    if (response.Headers.TryGetValue(name, out var value))
                    {
                        context.Response.Headers[name] = value;
                    }
                }

                if (response.Status < 400 && camera != null)
                {
                    Audit(context, camera);
                }

                context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()?.DisableBuffering();
                try
                {
                    await response.Body.CopyToAsync(context.Response.Body, context.RequestAborted);
                }
                catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
                {
                    // The viewer left: disposing the response tells the gateway to stop.
                }
                catch (IOException)
                {
                    // The gateway went away mid-response (or dropped a viewer that fell behind).
                    context.Abort();
                }
            }
        }

        /// <summary>The endpoint's audit, as if it had run here.</summary>
        private static void Audit(HttpContext context, CameraEntry camera)
        {
            var audit = context.GetEndpoint()?.Metadata.GetMetadata<AuditMetadata>();
            if (audit == null)
            {
                return;
            }

            var log = context.RequestServices.GetRequiredService<AuditLog>();
            if (!audit.Views || log.IsNewView(context.User, audit.Action, camera.Id))
            {
                log.Record(context, audit.Action, audit.TargetType, camera.Id, camera.Name, null);
            }
        }

        private static Task Problem(HttpContext context, int status, string detail)
        {
            context.Response.StatusCode = status;
            return context.Response.WriteAsJsonAsync(new { status, detail });
        }

        #endregion
    }
}
