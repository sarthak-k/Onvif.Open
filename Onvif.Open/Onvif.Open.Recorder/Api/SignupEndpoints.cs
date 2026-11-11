// Sarthak Killedar
// 2026-11-10

using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// The cloud: which region this is (and where the others are), self-service sign-up with an emailed link, and its
    /// confirmation, which creates the organization and signs its owner in.
    /// </summary>
    internal static class SignupEndpoints
    {
        public sealed class VerifyRequest
        {
            public string Token { get; set; }
        }

        public static IEndpointRouteBuilder MapSignupApi(this IEndpointRouteBuilder app)
        {
            app.MapGet("/api/cloud", (CloudOptions cloud, SignupService signup) => new
            {
                enabled = cloud.Enabled,
                region = cloud.Enabled ? cloud.Region : null,
                signup = signup.IsAvailable,
                regions = cloud.Enabled
                    ? Regions.All.Where(r => cloud.Regions.ContainsKey(r.Id)).Select(r => new { id = r.Id, name = r.Name, webUrl = cloud.Regions[r.Id].WebUrl.TrimEnd('/') })
                    : null
            }).AllowAnonymous();

            app.MapPost("/api/signup", async (SignupRequest request, SignupService signup, CancellationToken ct) =>
            {
                var result = await signup.StartAsync(request, ct);
                return result.Status == ServiceStatus.Ok
                    ? Results.Accepted(value: new { message = "Check your email: the link to finish signing up is on its way." })
                    : TenancyEndpoints.ToResult(result);
            }).AllowAnonymous().RequireRateLimiting(SecurityConfiguration.SignInLimit);

            app.MapPost("/api/signup/verify", async (VerifyRequest request, SignupService signup, AuditLog audit, CloudOptions cloud, HttpContext http, CancellationToken ct) =>
            {
                var result = await signup.VerifyAsync(request?.Token, ct);
                if (result.Status == ServiceStatus.NotFound)
                {
                    return Results.Json(new { errors = new[] { "This link has expired or was used already: sign up again, or sign in." } }, statusCode: StatusCodes.Status404NotFound);
                }

                if (result.Status != ServiceStatus.Ok)
                {
                    return TenancyEndpoints.ToResult(result);
                }

                audit.Record(http, result.Value, null, null, AuditActions.Signup, new { region = cloud.Region });
                return AuthEndpoints.SignIn(result.Value);
            }).AllowAnonymous().RequireRateLimiting(SecurityConfiguration.SignInLimit);

            return app;
        }
    }
}
