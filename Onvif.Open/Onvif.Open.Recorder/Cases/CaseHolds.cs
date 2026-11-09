// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Cases
{
    public static class CaseStatuses
    {
        public const string Open = "Open";
        public const string Closed = "Closed";

        public static string Normalize(string status) =>
            string.Equals(status, Open, StringComparison.OrdinalIgnoreCase) ? Open : string.Equals(status, Closed, StringComparison.OrdinalIgnoreCase) ? Closed : null;
    }

    /// <summary>
    /// The video held by open cases, in memory: retention never deletes a file that overlaps one of their clips, whatever
    /// the cameras' policies say. Reloaded whenever a case or a clip changes.
    /// </summary>
    public sealed class CaseHolds
    {
        public CaseHolds(ICaseStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        private readonly ICaseStore _store;
        private readonly SemaphoreSlim _reload = new SemaphoreSlim(1, 1);
        private IReadOnlyDictionary<string, (DateTime From, DateTime To)[]> _held = new Dictionary<string, (DateTime, DateTime)[]>();

        public int Count => Volatile.Read(ref _held).Values.Sum(v => v.Length);

        public async Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            await _reload.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var clips = await _store.GetHeldClipsAsync(cancellationToken).ConfigureAwait(false);
                var held = clips
                    .SelectMany(c => CameraIds(c).Select(camera => (camera, c.From, c.To)))
                    .GroupBy(h => h.camera, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.Select(h => (h.From, h.To)).ToArray(), StringComparer.OrdinalIgnoreCase);
                Volatile.Write(ref _held, held);
            }
            finally
            {
                _reload.Release();
            }
        }

        /// <summary>Whether an open case holds part of [from, to] of a camera.</summary>
        public bool Holds(string cameraId, DateTimeOffset from, DateTimeOffset to)
        {
            if (cameraId == null || !Volatile.Read(ref _held).TryGetValue(cameraId, out var spans))
            {
                return false;
            }

            var start = from.UtcDateTime;
            var end = to.UtcDateTime;
            return spans.Any(s => s.From <= end && s.To >= start);
        }

        public static IReadOnlyList<string> CameraIds(CaseClipRecord clip) =>
            string.IsNullOrEmpty(clip?.Cameras) ? Array.Empty<string>() : JsonSerializer.Deserialize<List<string>>(clip.Cameras) ?? new List<string>();
    }
}
