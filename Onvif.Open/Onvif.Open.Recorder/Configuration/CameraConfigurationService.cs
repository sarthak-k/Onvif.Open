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
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Configuration
{
    /// <summary>
    /// Adds, changes and removes cameras while the recorder runs: validates, saves to the settings store, then
    /// applies the change to recording (a changed camera reconnects with its new settings).
    /// </summary>
    public sealed class CameraConfigurationService
    {
        #region Constructor

        /// <param name="remote">Sends cameras of other gateways to them (null: this recorder records every camera).</param>
        public CameraConfigurationService(ISettingsStore store, ITenancyStore tenancy, CameraDirectory directory, ConfiguredRecording recording, Gateways.GatewayService gateways, ILogger<CameraConfigurationService> logger, Tunnel.RemoteCameras remote = null)
        {
            _remote = remote;
            _gateways = gateways ?? throw new ArgumentNullException(nameof(gateways));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _tenancy = tenancy ?? throw new ArgumentNullException(nameof(tenancy));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _logger = logger;
        }

        #endregion

        #region Private Variables

        public const int MaxNameLength = 100;

        private readonly ISettingsStore _store;
        private readonly ITenancyStore _tenancy;
        private readonly CameraDirectory _directory;
        private readonly ConfiguredRecording _recording;
        private readonly Gateways.GatewayService _gateways;
        private readonly Tunnel.RemoteCameras _remote;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _changes = new SemaphoreSlim(1, 1);

        #endregion

        #region Public Methods

        /// <summary>The organization's cameras (also disabled ones), with their passwords: never return those to clients.</summary>
        public async Task<IReadOnlyList<CameraOptions>> GetCamerasAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            return (await _store.GetCamerasAsync(cancellationToken).ConfigureAwait(false)).Where(c => c.OrganizationId == organizationId).ToList();
        }

        /// <summary>The camera when it belongs to the organization, else null.</summary>
        public async Task<CameraOptions> GetCameraAsync(string organizationId, string cameraId, CancellationToken cancellationToken = default)
        {
            var camera = await _store.GetCameraAsync(cameraId, cancellationToken).ConfigureAwait(false);
            return camera?.OrganizationId == organizationId ? camera : null;
        }

        /// <summary>Adds a camera to a site of the organization; the recorder assigns its id.</summary>
        public async Task<ConfigurationResult> AddAsync(string organizationId, CameraOptions camera, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                camera.Id = Guid.NewGuid().ToString("D");
                camera.OrganizationId = organizationId;

                // On the gateway asked for (one of the organization's), else on this recorder's own.
                var local = (await _gateways.EnsureLocalAsync(cancellationToken).ConfigureAwait(false))?.Id;
                var requested = camera.GatewayId;
                camera.GatewayId = local;
                if (!string.IsNullOrEmpty(requested) && !string.Equals(requested, local, StringComparison.OrdinalIgnoreCase))
                {
                    if (_remote == null || await _gateways.FindAsync(organizationId, requested, cancellationToken).ConfigureAwait(false) == null)
                    {
                        return ConfigurationResult.Invalid(new[] { "Choose one of your gateways for the camera." });
                    }

                    camera.GatewayId = requested;
                }
                else if (local == null)
                {
                    // The cloud records nothing itself.
                    return ConfigurationResult.Invalid(new[] { "Choose the gateway that records the camera: at its site, or one in the cloud." });
                }

                var (policy, errors) = await PrepareAsync(organizationId, camera, cancellationToken).ConfigureAwait(false);
                if (errors.Count > 0)
                {
                    return ConfigurationResult.Invalid(errors);
                }

                // Start it first: a camera the recorder rejects is not saved. Another gateway gets it once saved.
                if (IsLocal(camera))
                {
                    try
                    {
                        _recording.AddCamera(camera);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is UriFormatException || ex is InvalidOperationException || ex is NotSupportedException)
                    {
                        return ConfigurationResult.Invalid(new[] { $"Camera '{camera.Name}': {ex.Message}" });
                    }
                }

                await _store.SaveCameraAsync(camera, cancellationToken).ConfigureAwait(false);
                _directory.Set(camera, organizationId, policy);
                await SyncAsync(camera, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Camera {CameraId} ({Name}) added", camera.Id, camera.Name);
                return ConfigurationResult.Ok(camera);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>
        /// Replaces a camera's configuration (name, site, policy, connection). A null <see cref="CameraOptions.Password"/>
        /// keeps the stored password, an empty one removes it. The id never changes.
        /// </summary>
        public async Task<ConfigurationResult> UpdateAsync(string organizationId, string cameraId, CameraOptions camera, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var existing = await GetCameraAsync(organizationId, cameraId, cancellationToken).ConfigureAwait(false);
                if (existing == null)
                {
                    return ConfigurationResult.NotFound();
                }

                camera.Id = existing.Id;
                camera.OrganizationId = organizationId;
                camera.GatewayId = existing.GatewayId;
                camera.Password = camera.Password == null ? existing.Password : camera.Password.Length == 0 ? null : camera.Password;

                var (policy, errors) = await PrepareAsync(organizationId, camera, cancellationToken).ConfigureAwait(false);
                if (errors.Count > 0)
                {
                    return ConfigurationResult.Invalid(errors);
                }

                var result = IsLocal(camera) ? await RestartAsync(existing, camera, cancellationToken).ConfigureAwait(false) : null;
                if (result != null)
                {
                    return result;
                }

                await _store.SaveCameraAsync(camera, cancellationToken).ConfigureAwait(false);
                _directory.Set(camera, organizationId, policy);
                await SyncAsync(camera, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Camera {CameraId} ({Name}) changed", camera.Id, camera.Name);
                return ConfigurationResult.Ok(camera);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>Removes a camera; its recordings stay until retention removes them. Refused while plugins use it.</summary>
        public async Task<ConfigurationResult> RemoveAsync(string organizationId, string cameraId, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var existing = await GetCameraAsync(organizationId, cameraId, cancellationToken).ConfigureAwait(false);
                if (existing == null)
                {
                    return ConfigurationResult.NotFound();
                }

                // An emptied camera list would mean "all cameras": the plugin must be reassigned explicitly.
                var plugins = (await _store.GetPluginsAsync(cancellationToken).ConfigureAwait(false)).Where(p => p.Cameras.Contains(existing.Id, StringComparer.OrdinalIgnoreCase)).Select(p => p.Folder).ToList();
                if (plugins.Count > 0)
                {
                    return ConfigurationResult.Conflict($"Camera '{existing.Name}' is assigned to plugin(s) {string.Join(", ", plugins)}: remove it from them first.");
                }

                if (IsLocal(existing))
                {
                    await _recording.RemoveCameraAsync(existing.Id, cancellationToken).ConfigureAwait(false);
                }

                await _store.DeleteCameraAsync(existing.Id, cancellationToken).ConfigureAwait(false);
                _directory.Remove(existing.Id);
                await SyncAsync(existing, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Camera {CameraId} ({Name}) removed", existing.Id, existing.Name);
                return ConfigurationResult.Ok(existing);
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>A recording policy changed: its cameras take the new mode and pre-/post-roll, and its retention.</summary>
        public async Task ApplyPolicyAsync(RecordingPolicyRecord policy, CancellationToken cancellationToken = default)
        {
            await _changes.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var remoteGateways = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var existing in (await _store.GetCamerasAsync(cancellationToken).ConfigureAwait(false)).Where(c => c.PolicyId == policy.Id))
                {
                    var camera = RecordingPolicyRules.Apply(Copy(existing), policy);
                    if (!IsLocal(camera))
                    {
                        remoteGateways.Add(camera.GatewayId);
                    }
                    else if (camera.Mode != existing.Mode || camera.PreRoll != existing.PreRoll || camera.PostRoll != existing.PostRoll)
                    {
                        await RestartAsync(existing, camera, cancellationToken).ConfigureAwait(false);
                    }

                    _directory.Set(camera, camera.OrganizationId, policy);
                }

                foreach (var gateway in remoteGateways)
                {
                    await _remote.SyncAsync(gateway, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _changes.Release();
            }
        }

        /// <summary>The cameras as the recorder runs them: each with its policy applied, and the directory filled.</summary>
        public static IReadOnlyList<CameraOptions> Resolve(IReadOnlyList<CameraOptions> cameras, IReadOnlyList<RecordingPolicyRecord> policies, CameraDirectory directory)
        {
            var byId = policies.ToDictionary(p => p.Id, StringComparer.Ordinal);
            var resolved = new List<CameraOptions>();

            foreach (var camera in cameras)
            {
                if (camera.PolicyId == null || !byId.TryGetValue(camera.PolicyId, out var policy))
                {
                    continue;
                }

                RecordingPolicyRules.Apply(camera, policy);
                directory.Set(camera, camera.OrganizationId, policy);
                resolved.Add(camera);
            }

            return resolved;
        }

        #endregion

        #region Private Methods

        /// <summary>Validates name, site and policy (both of this organization; the default policy when none is given) and applies the policy.</summary>
        private async Task<(RecordingPolicyRecord Policy, List<string> Errors)> PrepareAsync(string organizationId, CameraOptions camera, CancellationToken cancellationToken)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(camera.Name) || camera.Name.Trim().Length > MaxNameLength)
            {
                errors.Add($"Name must be 1 to {MaxNameLength} characters.");
            }
            else
            {
                camera.Name = camera.Name.Trim();
            }

            var site = string.IsNullOrEmpty(camera.SiteId) ? null : await _tenancy.FindSiteAsync(camera.SiteId, cancellationToken).ConfigureAwait(false);
            if (site?.OrganizationId != organizationId)
            {
                errors.Add("Choose one of your sites for the camera.");
            }

            var policies = await _tenancy.GetPoliciesAsync(organizationId, cancellationToken).ConfigureAwait(false);
            var policy = string.IsNullOrEmpty(camera.PolicyId) ? policies.FirstOrDefault(p => p.IsDefault) : policies.FirstOrDefault(p => p.Id == camera.PolicyId);
            if (policy == null)
            {
                errors.Add("Choose one of your recording policies for the camera.");
            }
            else
            {
                camera.PolicyId = policy.Id;
                RecordingPolicyRules.Apply(camera, policy);
            }

            errors.AddRange(RecordingConfiguration.ValidateCamera(camera));
            return (policy, errors);
        }

        /// <summary>Whether this recorder records the camera itself (else another gateway does).</summary>
        private bool IsLocal(CameraOptions camera) =>
            _remote == null || camera.GatewayId == null || string.Equals(camera.GatewayId, _gateways.Local?.Id, StringComparison.OrdinalIgnoreCase);

        /// <summary>Another gateway's camera changed: it gets its new set (now, or when its tunnel opens).</summary>
        private Task SyncAsync(CameraOptions camera, CancellationToken cancellationToken) =>
            IsLocal(camera) ? Task.CompletedTask : _remote.SyncAsync(camera.GatewayId, cancellationToken);

        /// <summary>Restarts the camera's recorder with new settings; on failure, the old ones are restored and the reason returned.</summary>
        private async Task<ConfigurationResult> RestartAsync(CameraOptions existing, CameraOptions camera, CancellationToken cancellationToken)
        {
            await _recording.RemoveCameraAsync(existing.Id, cancellationToken).ConfigureAwait(false);

            try
            {
                _recording.AddCamera(camera);
                return null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is UriFormatException || ex is InvalidOperationException || ex is NotSupportedException)
            {
                _recording.AddCamera(existing);
                return ConfigurationResult.Invalid(new[] { $"Camera '{camera.Name}': {ex.Message}" });
            }
        }

        private static CameraOptions Copy(CameraOptions camera)
        {
            var json = JsonSerializer.Serialize(camera);
            var copy = JsonSerializer.Deserialize<CameraOptions>(json);
            copy.Password = camera.Password;
            return copy;
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
