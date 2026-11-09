// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Onvif.Open.Recorder.Security;

namespace Onvif.Open.Recorder.Tenancy
{
    /// <summary>
    /// What a signed-in user may see: their organization, and within it all sites or only the ones they were given.
    /// </summary>
    public static class CameraAccess
    {
        public static string OrganizationId(this ClaimsPrincipal user) => user.FindFirstValue(RecorderClaims.Organization);

        /// <summary>The sites the user is limited to; null: all sites of the organization.</summary>
        public static IReadOnlySet<string> Sites(this ClaimsPrincipal user)
        {
            var sites = user.FindFirstValue(RecorderClaims.Sites);
            return sites == null ? null : new HashSet<string>(sites.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>True for users who see every site (organization-wide settings need it).</summary>
        public static bool HasAllSites(this ClaimsPrincipal user) => user.FindFirstValue(RecorderClaims.Sites) == null;

        public static bool CanSeeSite(this ClaimsPrincipal user, string organizationId, string siteId)
        {
            if (organizationId == null || !string.Equals(organizationId, user.OrganizationId(), StringComparison.Ordinal))
            {
                return false;
            }

            var sites = user.Sites();
            return sites == null || (siteId != null && sites.Contains(siteId));
        }

        public static bool CanSee(this ClaimsPrincipal user, CameraEntry camera)
        {
            return camera != null && user.CanSeeSite(camera.OrganizationId, camera.SiteId);
        }

        public static bool CanSeeCamera(this ClaimsPrincipal user, CameraDirectory directory, string cameraId)
        {
            return user.CanSee(directory.Find(cameraId));
        }
    }
}
