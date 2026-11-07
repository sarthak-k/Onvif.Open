// Sarthak Killedar
// 2026-11-07

using System;
using System.Text.RegularExpressions;

namespace Onvif.Open.Recording.Implementation.Storage
{
    /// <summary>
    /// Camera ids become directory and file names, so they are restricted to a safe character set.
    /// </summary>
    public static class CameraIdentifier
    {
        #region Private Variables

        private static readonly Regex Valid = new Regex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public Methods

        public static bool IsValid(string cameraId)
        {
            return cameraId != null && Valid.IsMatch(cameraId);
        }

        public static string Require(string cameraId)
        {
            if (!IsValid(cameraId))
            {
                throw new ArgumentException("Camera id must be 1-64 characters of letters, digits, '-' or '_' and start with a letter or digit.", nameof(cameraId));
            }

            return cameraId;
        }

        #endregion
    }
}
