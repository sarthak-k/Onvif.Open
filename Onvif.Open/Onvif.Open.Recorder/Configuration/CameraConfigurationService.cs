// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Configuration
{
    /// <summary>
    /// Adds, changes and removes cameras while the recorder runs: validates, saves to the settings store, then
    /// applies the change to recording (a changed camera reconnects with its new settings).
    /// </summary>
    public sealed class CameraConfigurationService
    {
        #region Constructor

        public CameraConfigurationService(ISettingsStore store, ConfiguredRecording recording, ILogger<CameraConfigurationService> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _logger = logger;
        }

        #endregion

        #region Private Variables

        private readonly ISettingsStore _store;
        private readonly ConfiguredRecording _recording;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);

        #endregion

        #region Public Methods

        /// <summary>All configured cameras (also disabled ones), with their passwords: never return those to clients.</summary>
        public Task<IReadOnlyList<CameraOptions>> GetCamerasAsync(CancellationToken cancellationToken = default)
        {
            return _store.GetCamerasAsync(cancellationToken);
        }

        public Task<CameraOptions> GetCameraAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            return _store.GetCameraAsync(cameraId, cancellationToken);
        }

        public async Task<ConfigurationResult> AddAsync(CameraOptions camera, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var errors = RecordingConfiguration.ValidateCamera(camera);
                if (errors.Count > 0)
                {
                    return ConfigurationResult.Invalid(errors);
                }

                if (await _store.GetCameraAsync(camera.Id, cancellationToken).ConfigureAwait(false) != null)
                {
                    return ConfigurationResult.Conflict($"Camera '{camera.Id}' already exists.");
                }

                // Start it first: a camera the recorder rejects is not saved.
                try
                {
                    _recording.AddCamera(camera);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is UriFormatException || ex is InvalidOperationException || ex is NotSupportedException)
                {
                    return ConfigurationResult.Invalid(new[] { $"Camera '{camera.Id}': {ex.Message}" });
                }

                await _store.SaveCameraAsync(camera, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Camera {CameraId} added", camera.Id);
                return ConfigurationResult.Ok(camera);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>
        /// Replaces a camera's configuration. A null <see cref="CameraOptions.Password"/> keeps the stored password,
        /// an empty one removes it.
        /// </summary>
        public async Task<ConfigurationResult> UpdateAsync(string cameraId, CameraOptions camera, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var existing = await _store.GetCameraAsync(cameraId, cancellationToken).ConfigureAwait(false);
                if (existing == null)
                {
                    return ConfigurationResult.NotFound();
                }

                if (camera.Id != null && !string.Equals(camera.Id, existing.Id, StringComparison.OrdinalIgnoreCase))
                {
                    return ConfigurationResult.Invalid(new[] { "The camera id cannot be changed: add a new camera instead." });
                }

                camera.Id = existing.Id;
                camera.Password = camera.Password == null ? existing.Password : camera.Password.Length == 0 ? null : camera.Password;

                var errors = RecordingConfiguration.ValidateCamera(camera);
                if (errors.Count > 0)
                {
                    return ConfigurationResult.Invalid(errors);
                }

                // Restart the camera's recorder with the new settings; on failure, the old ones are restored.
                await _recording.RemoveCameraAsync(existing.Id, cancellationToken).ConfigureAwait(false);

                try
                {
                    _recording.AddCamera(camera);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is UriFormatException || ex is InvalidOperationException || ex is NotSupportedException)
                {
                    _recording.AddCamera(existing);
                    return ConfigurationResult.Invalid(new[] { $"Camera '{camera.Id}': {ex.Message}" });
                }

                await _store.SaveCameraAsync(camera, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Camera {CameraId} changed", camera.Id);
                return ConfigurationResult.Ok(camera);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Removes a camera; its recordings stay until retention removes them. Refused while plugins use it.</summary>
        public async Task<ConfigurationResult> RemoveAsync(string cameraId, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var existing = await _store.GetCameraAsync(cameraId, cancellationToken).ConfigureAwait(false);
                if (existing == null)
                {
                    return ConfigurationResult.NotFound();
                }

                // An emptied camera list would mean "all cameras": the plugin must be reassigned explicitly.
                var plugins = (await _store.GetPluginsAsync(cancellationToken).ConfigureAwait(false)).Where(p => p.Cameras.Contains(existing.Id, StringComparer.OrdinalIgnoreCase)).Select(p => p.Folder).ToList();
                if (plugins.Count > 0)
                {
                    return ConfigurationResult.Conflict($"Camera '{existing.Id}' is assigned to plugin(s) {string.Join(", ", plugins)}: remove it from them first.");
                }

                await _recording.RemoveCameraAsync(existing.Id, cancellationToken).ConfigureAwait(false);
                await _store.DeleteCameraAsync(existing.Id, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Camera {CameraId} removed", existing.Id);
                return ConfigurationResult.Ok(existing);
            }
            finally
            {
                _changes.Release();
            }
        }

        #endregion
    }

    /// <summary>
    /// Outcome of a configuration change.
    /// </summary>
    public sealed class ConfigurationResult
    {
        #region Constructor

        private ConfigurationResult(ConfigurationStatus status, CameraOptions camera, IReadOnlyList<string> errors)
        {
            Status = status;
            Camera = camera;
            Errors = errors ?? Array.Empty<string>();
        }

        #endregion

        #region Public Variables

        public ConfigurationStatus Status { get; }
        public CameraOptions Camera { get; }
        public IReadOnlyList<string> Errors { get; }

        #endregion

        #region Public Methods

        public static ConfigurationResult Ok(CameraOptions camera) => new ConfigurationResult(ConfigurationStatus.Ok, camera, null);
        public static ConfigurationResult NotFound() => new ConfigurationResult(ConfigurationStatus.NotFound, null, null);
        public static ConfigurationResult Conflict(string error) => new ConfigurationResult(ConfigurationStatus.Conflict, null, new[] { error });
        public static ConfigurationResult Invalid(IEnumerable<string> errors) => new ConfigurationResult(ConfigurationStatus.Invalid, null, errors.ToList());

        #endregion
    }

    public enum ConfigurationStatus
    {
        Ok,
        NotFound,
        Conflict,
        Invalid
    }
}
