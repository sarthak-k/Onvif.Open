// Sarthak Killedar
// 2026-11-08

using System.Collections.Generic;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// Durable configuration that changes while the recorder runs: cameras and plugin assignments.
    /// Secrets (camera passwords, plugin settings) are encrypted at rest.
    /// </summary>
    public interface ISettingsStore
    {
        #region Public Methods

        /// <summary>All cameras in the order they were added, with their passwords.</summary>
        IReadOnlyList<CameraOptions> GetCameras();

        /// <summary>One camera with its password, or null.</summary>
        CameraOptions GetCamera(string cameraId);

        /// <summary>Inserts the camera or replaces the one with the same id (case-insensitive).</summary>
        void SaveCamera(CameraOptions camera);

        bool DeleteCamera(string cameraId);

        /// <summary>Plugin folders with their options and settings.</summary>
        IReadOnlyList<StoredPlugin> GetPlugins();

        void SavePlugin(StoredPlugin plugin);

        /// <summary>True once the configuration file has been imported (the store is the source of truth from then on).</summary>
        bool IsImported { get; }

        /// <summary>Saves the cameras and plugins of the configuration file and marks the store as imported, in one transaction.</summary>
        void Import(IEnumerable<CameraOptions> cameras, IEnumerable<StoredPlugin> plugins);

        #endregion
    }

    /// <summary>
    /// A plugin folder: whether it runs, its cameras (empty: all) and its settings.
    /// </summary>
    public sealed class StoredPlugin
    {
        #region Public Variables

        public string Folder { get; set; }
        public bool Enabled { get; set; } = true;
        public List<string> Cameras { get; set; } = new List<string>();
        public Dictionary<string, string> Settings { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        #endregion
    }
}
