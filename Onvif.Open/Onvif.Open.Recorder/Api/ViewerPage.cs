// Sarthak Killedar
// 2026-11-07

using System.IO;
using System.Reflection;

namespace Onvif.Open.Recorder.Api
{
    /// <summary>
    /// The web viewer (live view, playback, clip download), embedded in the assembly so deployment stays one folder.
    /// </summary>
    internal static class ViewerPage
    {
        #region Public Variables

        public static string Html { get; } = Load();

        #endregion

        #region Private Methods

        private static string Load()
        {
            using (var stream = typeof(ViewerPage).Assembly.GetManifestResourceStream("Onvif.Open.Recorder.Web.viewer.html"))
            using (var reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }

        #endregion
    }
}
