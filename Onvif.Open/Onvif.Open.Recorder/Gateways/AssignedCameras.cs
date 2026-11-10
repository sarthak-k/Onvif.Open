// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Tenancy;
using Onvif.Open.Recorder.Tunnel;

namespace Onvif.Open.Recorder.Gateways
{
    /// <summary>
    /// The cameras the control plane has this gateway record. Each new set replaces the last: cameras that changed are
    /// restarted, others keep recording untouched. The set is kept on disk (encrypted: it holds camera passwords), so the
    /// gateway records through a cloud outage and after a restart before the tunnel is back.
    /// </summary>
    public sealed class AssignedCameras
    {
        public AssignedCameras(ConfiguredRecording recording, CameraDirectory directory, IDataProtectionProvider dataProtection, string path, ILogger<AssignedCameras> logger)
        {
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _protector = (dataProtection ?? throw new ArgumentNullException(nameof(dataProtection))).CreateProtector("Onvif.Open.Recorder.AssignedCameras");
            _path = path;
            _logger = logger;
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        private readonly ConfiguredRecording _recording;
        private readonly CameraDirectory _directory;
        private readonly IDataProtector _protector;
        private readonly string _path;
        private readonly ILogger _logger;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private Dictionary<string, string> _applied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> CameraIds => _applied.Keys.ToList();

        /// <summary>The set kept on disk, applied at startup (nothing when there is none).</summary>
        public async Task LoadAsync(CancellationToken cancellationToken = default)
        {
            if (_path == null || !File.Exists(_path))
            {
                return;
            }

            try
            {
                var cameras = JsonSerializer.Deserialize<List<AssignedCamera>>(_protector.Unprotect(await File.ReadAllTextAsync(_path, cancellationToken).ConfigureAwait(false)), Json);
                await ApplyAsync(cameras, persist: false, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is JsonException || ex is System.Security.Cryptography.CryptographicException || ex is IOException)
            {
                _logger?.LogWarning("The cameras kept from the control plane could not be read ({Error}): waiting for it", ex.Message);
            }
        }

        /// <summary>Records exactly these cameras (for the control plane), and keeps the set on disk.</summary>
        public Task ApplyAsync(IReadOnlyList<AssignedCamera> cameras, CancellationToken cancellationToken = default) => ApplyAsync(cameras, true, cancellationToken);

        private async Task ApplyAsync(IReadOnlyList<AssignedCamera> cameras, bool persist, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var assigned in (cameras ?? Array.Empty<AssignedCamera>()).Where(c => c?.Camera?.Id != null && c.Policy != null))
                {
                    var camera = assigned.Camera;
                    var fingerprint = JsonSerializer.Serialize(assigned, Json);
                    next[camera.Id] = fingerprint;
                    if (_applied.TryGetValue(camera.Id, out var previous) && previous == fingerprint)
                    {
                        continue;
                    }

                    if (_applied.ContainsKey(camera.Id))
                    {
                        await _recording.RemoveCameraAsync(camera.Id, cancellationToken).ConfigureAwait(false);
                    }

                    try
                    {
                        _recording.AddCamera(camera);
                        _directory.Set(camera, camera.OrganizationId, assigned.Policy);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is UriFormatException || ex is InvalidOperationException || ex is NotSupportedException)
                    {
                        _logger?.LogWarning("Camera {CameraId} from the control plane cannot be recorded: {Error}", camera.Id, ex.Message);
                        next.Remove(camera.Id);
                    }
                }

                foreach (var removed in _applied.Keys.Where(id => !next.ContainsKey(id)).ToList())
                {
                    await _recording.RemoveCameraAsync(removed, cancellationToken).ConfigureAwait(false);
                    _directory.Remove(removed);
                }

                _applied = next;
                if (persist && _path != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path)));
                    await File.WriteAllTextAsync(_path, _protector.Protect(JsonSerializer.Serialize(cameras ?? Array.Empty<AssignedCamera>(), Json)), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
