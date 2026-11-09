// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Plugin;
using Onvif.Open.Recorder.Configuration;

using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// REST API: plugin status, plugin events (linked to the recording that covers them) and camera snapshots.
    /// </summary>
    internal static class PluginEndpoints
    {
        #region Public Methods

        public static IEndpointRouteBuilder MapPluginApi(this IEndpointRouteBuilder app)
        {
            var api = app.MapGroup("/api").RequireAuthorization(Policies.Viewer);

            api.MapGet("/plugins", (IPluginHost host) => host.GetStatus()).RequireAuthorization(Policies.Admin);

            api.MapGet("/cameras/{id}/snapshot", async (string id, IRecordingManager manager, ConfiguredRecording recording, CancellationToken ct) =>
            {
                if (!manager.GetStatus().Any(s => string.Equals(s.CameraId, id, StringComparison.OrdinalIgnoreCase)))
                {
                    return Results.NotFound();
                }

                try
                {
                    return Results.File(await recording.GetSnapshotAsync(id, ct), "image/jpeg");
                }
                catch (NotSupportedException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status501NotImplemented);
                }
                catch (HttpRequestException ex)
                {
                    return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
                }
            });

            return app;
        }

        #endregion
    }
}
