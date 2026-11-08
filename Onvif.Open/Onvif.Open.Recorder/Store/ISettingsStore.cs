// Sarthak Killedar
// 2026-11-08

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// Durable settings that change while the recorder runs: cameras and plugin assignments.
    /// Secrets (camera passwords, plugin settings) are encrypted before they reach the database.
    /// </summary>
    public interface ISettingsStore
    {
        #region Public Methods

        /// <summary>All cameras in the order they were added, with their passwords.</summary>
        Task<IReadOnlyList<CameraOptions>> GetCamerasAsync(CancellationToken cancellationToken = default);

        /// <summary>One camera (id case-insensitive) with its password, or null.</summary>
        Task<CameraOptions> GetCameraAsync(string cameraId, CancellationToken cancellationToken = default);

        /// <summary>Inserts the camera or replaces the one with the same id; the id keeps its first spelling.</summary>
        Task SaveCameraAsync(CameraOptions camera, CancellationToken cancellationToken = default);

        Task<bool> DeleteCameraAsync(string cameraId, CancellationToken cancellationToken = default);

        /// <summary>Plugin folders with their options and settings.</summary>
        Task<IReadOnlyList<StoredPlugin>> GetPluginsAsync(CancellationToken cancellationToken = default);

        Task SavePluginAsync(StoredPlugin plugin, CancellationToken cancellationToken = default);

        Task<bool> DeletePluginAsync(string folder, CancellationToken cancellationToken = default);

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
