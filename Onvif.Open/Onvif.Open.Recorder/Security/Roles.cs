// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Recorder.Security
{
    /// <summary>
    /// The roles of recorder users. Each includes the ones before it: an operator can do everything a viewer can.
    /// </summary>
    public static class Roles
    {
        #region Public Variables

        /// <summary>Watches live video and recordings, downloads clips, sees camera status.</summary>
        public const string Viewer = "Viewer";

        /// <summary>Also controls cameras: recording start/stop/trigger, PTZ, focus, relays.</summary>
        public const string Operator = "Operator";

        /// <summary>Also configures: sites, cameras, recording policies, plugins, users, storage, discovery.</summary>
        public const string Admin = "Admin";

        /// <summary>Also manages owners and the organization itself; at least one enabled owner always remains.</summary>
        public const string Owner = "Owner";

        public static IReadOnlyList<string> All { get; } = new[] { Viewer, Operator, Admin, Owner };

        #endregion

        #region Public Methods

        /// <summary>The role with its canonical spelling, or null for an unknown one.</summary>
        public static string Normalize(string role)
        {
            return All.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The roles that satisfy <paramref name="minimum"/> (it and the ones above it).</summary>
        public static string[] AtLeast(string minimum)
        {
            return All.Skip(Array.IndexOf(All.ToArray(), minimum)).ToArray();
        }

        #endregion
    }

    /// <summary>
    /// Authorization policies; apply the lowest role an endpoint needs.
    /// </summary>
    public static class Policies
    {
        public const string Viewer = "Viewer";
        public const string Operator = "Operator";
        public const string Admin = "Admin";
        public const string Owner = "Owner";

        /// <summary>An administrator (or owner) who sees every site: organization-wide settings (users, sites, policies).</summary>
        public const string OrganizationAdmin = "OrganizationAdmin";

        /// <summary>Requests the control plane sends through a gateway's tunnel.</summary>
        public const string ControlPlane = "ControlPlane";

        /// <summary>Signed in, even with a password that must be changed first (only for the account endpoints).</summary>
        public const string SignedIn = "SignedIn";
    }

    /// <summary>
    /// Claims of the recorder's tokens.
    /// </summary>
    public static class RecorderClaims
    {
        public const string SecurityStamp = "onvif:stamp";
        public const string MustChangePassword = "onvif:must_change_password";

        /// <summary>The organization requires a second factor the user has not set up: only that is allowed until then.</summary>
        public const string MustSetUpTwoFactor = "onvif:must_set_up_two_factor";

        /// <summary>The organization the token acts in.</summary>
        public const string Organization = "onvif:org";

        /// <summary>The site ids the user is limited to, separated by spaces; absent: all sites.</summary>
        public const string Sites = "onvif:sites";

        /// <summary>A request the control plane sent through a gateway's tunnel (never in a token).</summary>
        public const string Tunnel = "onvif:tunnel";
    }
}
