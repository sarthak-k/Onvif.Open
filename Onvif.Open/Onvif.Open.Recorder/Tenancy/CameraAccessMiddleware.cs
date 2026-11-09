// Sarthak Killedar
// 2026-11-09

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Onvif.Open.Recorder.Tenancy
{
    /// <summary>
    /// One check for every camera route (/api/cameras/{id}/... and /api/config/cameras/{id}): a camera of another
    /// organization, or of a site the user may not see, answers 404 as if it did not exist. Lists filter the same way.
    /// </summary>
    public static class CameraAccessMiddleware
    {
        public static WebApplication UseCameraAccess(this WebApplication app)
        {
            app.Use(async (context, next) =>
            {
                var cameraId = CameraIdOf(context.Request.Path);

                if (cameraId != null && context.User.Identity?.IsAuthenticated == true)
                {
                    var directory = context.RequestServices.GetRequiredService<CameraDirectory>();
                    if (!context.User.CanSeeCamera(directory, cameraId))
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        return;
                    }
                }

                await next();
            });

            return app;
        }

        /// <summary>The camera id a path addresses, or null.</summary>
        public static string CameraIdOf(PathString path)
        {
            foreach (var prefix in new[] { "/api/cameras/", "/api/config/cameras/" })
            {
                if (path.StartsWithSegments(prefix.TrimEnd('/'), StringComparison.OrdinalIgnoreCase, out var rest) && rest.HasValue)
                {
                    var id = rest.Value.TrimStart('/').Split('/')[0];
                    // "test" is the connection test of an unsaved camera, not a camera.
                    return id.Length == 0 || (prefix == "/api/config/cameras/" && id.Equals("test", StringComparison.OrdinalIgnoreCase)) ? null : Uri.UnescapeDataString(id);
                }
            }

            return null;
        }
    }
}
