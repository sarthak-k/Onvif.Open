// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Recorder.Tenancy
{
    /// <summary>
    /// "Cloud": this recorder is a multi-tenant control plane for one data region. Customers sign up themselves (with
    /// their email, verified); there is no first-run setup, and this recorder records no cameras itself: every camera is
    /// on a gateway (at the customer's site, or one we host for them). Each region is a separate deployment with its own
    /// database: an organization's data never leaves the region it chose.
    /// </summary>
    public sealed class CloudOptions
    {
        public const string SectionName = "Cloud";

        public bool Enabled { get; set; }

        /// <summary>This deployment's region (see <see cref="Regions"/>): every organization signed up here is in it.</summary>
        public string Region { get; set; }

        /// <summary>Every region's web app, by region id: sign-up and sign-in send people to theirs.</summary>
        public Dictionary<string, CloudRegion> Regions { get; set; } = new Dictionary<string, CloudRegion>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How long a sign-up link works.</summary>
        public TimeSpan SignupLifetime { get; set; } = TimeSpan.FromHours(24);

        /// <summary>Gateways hosted here for customers whose cameras are on the internet (one per customer).</summary>
        public Gateways.CloudGatewayOptions Gateways { get; set; } = new Gateways.CloudGatewayOptions();

        /// <summary>This region's web app (links in emails point there).</summary>
        public string WebUrl => Region != null && Regions.TryGetValue(Region, out var region) ? region.WebUrl?.TrimEnd('/') : null;

        /// <summary>Problems with the configuration (empty when it is valid, or the cloud is off).</summary>
        public IReadOnlyList<string> Check()
        {
            var errors = new List<string>();
            if (!Enabled)
            {
                return errors;
            }

            if (Tenancy.Regions.Normalize(Region) == null)
            {
                errors.Add($"Cloud:Region must be one of {string.Join(", ", Tenancy.Regions.All.Select(r => r.Id))}.");
            }

            foreach (var (id, region) in Regions)
            {
                if (Tenancy.Regions.Normalize(id) == null || !Uri.TryCreate(region?.WebUrl, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http"))
                {
                    errors.Add($"Cloud:Regions:{id} needs a known region id and a WebUrl (https).");
                }
            }

            if (WebUrl == null)
            {
                errors.Add("Cloud:Regions must include this deployment's own region, with its WebUrl.");
            }

            errors.AddRange(Gateways.Check());

            return errors;
        }
    }

    public sealed class CloudRegion
    {
        public string WebUrl { get; set; }
    }
}
