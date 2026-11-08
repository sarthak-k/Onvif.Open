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

        /// <summary>Also configures: cameras, plugins, users, storage, discovery.</summary>
        public const string Admin = "Admin";

        public static IReadOnlyList<string> All { get; } = new[] { Viewer, Operator, Admin };

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
    }
}
