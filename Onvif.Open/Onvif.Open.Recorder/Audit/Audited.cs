// Sarthak Killedar
// 2026-11-09

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Audit
{
    /// <summary>Kinds of things an audit entry is about; the names are looked up before the action (so deletions keep them).</summary>
    public static class AuditTargets
    {
        public const string Camera = "camera";
        public const string Site = "site";
        public const string Policy = "policy";
        public const string User = "user";
        public const string Layout = "layout";
        public const string Plugin = "plugin";
        public const string Organization = "organization";
        public const string Gateway = "gateway";
    }

    /// <summary>
    /// Records an endpoint's action in the audit log when it succeeds: who, what, which (id and name, from the route
    /// before the action, or from the response when it creates something) and optional details from its arguments.
    /// </summary>
    public static class Audited
    {
        /// <param name="routeId">The route value holding the target's id ("id", "folder").</param>
        /// <param name="views">Video viewing: recorded once per user and camera every <see cref="AuditLog.ViewWindow"/>.</param>
        /// <param name="details">Details from the endpoint's arguments and request (never secrets).</param>
        public static TBuilder Audit<TBuilder>(this TBuilder builder, string action, string targetType, string routeId = "id", bool views = false, Func<EndpointFilterInvocationContext, object> details = null)
            where TBuilder : IEndpointConventionBuilder
        {
            return builder.AddEndpointFilter(async (context, next) =>
            {
                var http = context.HttpContext;
                var log = http.RequestServices.GetRequiredService<AuditLog>();
                var id = http.GetRouteValue(routeId) as string;

                if (views && (id == null || !log.IsNewView(http.User, action, id)))
                {
                    return await next(context);
                }

                var nameBefore = id == null ? null : await NameOfAsync(http, targetType, id);
                var result = await next(context);

                if (Succeeded(result, http))
                {
                    var (createdId, name) = Describe(result);
                    log.Record(http, action, targetType, id ?? createdId, name ?? nameBefore, details?.Invoke(context));
                }

                return result;
            });
        }

        private static bool Succeeded(object result, HttpContext http)
        {
            if (http.Response.HasStarted)
            {
                return http.Response.StatusCode < 400;
            }

            return result switch
            {
                IStatusCodeHttpResult status => (status.StatusCode ?? 200) is >= 200 and < 300,
                IResult => true,
                _ => true
            };
        }

        /// <summary>The id and name in a response body ("id", and "name" or "userName"), when it has them.</summary>
        private static (string Id, string Name) Describe(object result)
        {
            var value = result is IValueHttpResult valued ? valued.Value : result is IResult ? null : result;
            if (value == null)
            {
                return (null, null);
            }

            try
            {
                var json = JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (json.ValueKind != JsonValueKind.Object)
                {
                    return (null, null);
                }

                string Text(string property) => json.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                return (Text("id"), Text("name") ?? Text("userName"));
            }
            catch (NotSupportedException)
            {
                return (null, null);
            }
        }

        private static async Task<string> NameOfAsync(HttpContext http, string targetType, string id)
        {
            var services = http.RequestServices;

            switch (targetType)
            {
                case AuditTargets.Camera:
                    return services.GetRequiredService<CameraDirectory>().Find(id)?.Name;
                case AuditTargets.Site:
                    return (await services.GetRequiredService<ITenancyStore>().FindSiteAsync(id))?.Name;
                case AuditTargets.Policy:
                    return (await services.GetRequiredService<ITenancyStore>().FindPolicyAsync(id))?.Name;
                case AuditTargets.User:
                    return (await services.GetRequiredService<IUserStore>().FindByIdAsync(id))?.UserName;
                case AuditTargets.Layout:
                    return (await services.GetRequiredService<ILayoutStore>().FindAsync(id))?.Name;
                case AuditTargets.Plugin:
                    return id;
                case AuditTargets.Gateway:
                    return (await services.GetRequiredService<Gateways.IGatewayStore>().FindAsync(id))?.Name;
                default:
                    return null;
            }
        }
    }
}
