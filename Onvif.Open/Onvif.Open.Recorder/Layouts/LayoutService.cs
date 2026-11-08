// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recording.Implementation.Storage;

namespace Onvif.Open.Recorder.Layouts
{
    /// <summary>
    /// Live view layouts: everyone keeps their own; administrators also manage the shared ones everybody sees.
    /// The grid's shape belongs to the web application; the recorder keeps its name and the camera of each tile.
    /// </summary>
    public sealed partial class LayoutService
    {
        #region Constructor

        public LayoutService(ILayoutStore store, TimeProvider timeProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Private Variables

        public const int MaxNameLength = 64;
        public const int MaxTiles = 64;
        public const int MaxLayoutsPerUser = 100;

        private readonly ILayoutStore _store;
        private readonly TimeProvider _timeProvider;

        [GeneratedRegex("^[a-z0-9][a-z0-9+x-]{0,31}$", RegexOptions.CultureInvariant)]
        private static partial Regex ValidGrid();

        #endregion

        #region Public Methods

        public async Task<IReadOnlyList<Layout>> GetAsync(LayoutUser user, CancellationToken cancellationToken = default)
        {
            return (await _store.GetVisibleAsync(user.Id, cancellationToken).ConfigureAwait(false)).Select(l => ToLayout(l, user)).ToList();
        }

        public async Task<LayoutResult> CreateAsync(LayoutUser user, LayoutRequest request, CancellationToken cancellationToken = default)
        {
            var errors = Validate(request);
            if (errors.Count > 0)
            {
                return LayoutResult.Invalid(errors);
            }

            if (request.Shared && !user.IsAdmin)
            {
                return LayoutResult.Forbidden();
            }

            if (!request.Shared && await _store.CountOwnedAsync(user.Id, cancellationToken).ConfigureAwait(false) >= MaxLayoutsPerUser)
            {
                return LayoutResult.Invalid(new[] { $"You can keep at most {MaxLayoutsPerUser} layouts." });
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var record = new LayoutRecord
            {
                Id = Guid.NewGuid().ToString(),
                OwnerId = request.Shared ? null : user.Id,
                CreatedAt = now
            };

            Apply(record, request, now);
            await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
            return LayoutResult.Ok(ToLayout(record, user));
        }

        /// <summary>Changes name, grid and cameras; whether a layout is shared stays as it was created.</summary>
        public async Task<LayoutResult> UpdateAsync(LayoutUser user, string id, LayoutRequest request, CancellationToken cancellationToken = default)
        {
            var record = await FindEditableAsync(user, id, cancellationToken).ConfigureAwait(false);
            if (record.Result != null)
            {
                return record.Result;
            }

            var errors = Validate(request);
            if (errors.Count > 0)
            {
                return LayoutResult.Invalid(errors);
            }

            Apply(record.Layout, request, _timeProvider.GetUtcNow().UtcDateTime);
            await _store.SaveAsync(record.Layout, cancellationToken).ConfigureAwait(false);
            return LayoutResult.Ok(ToLayout(record.Layout, user));
        }

        public async Task<LayoutResult> DeleteAsync(LayoutUser user, string id, CancellationToken cancellationToken = default)
        {
            var record = await FindEditableAsync(user, id, cancellationToken).ConfigureAwait(false);
            if (record.Result != null)
            {
                return record.Result;
            }

            await _store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
            return LayoutResult.Ok(null);
        }

        #endregion

        #region Private Methods

        /// <summary>Another user's private layout is reported as not found: its existence is not theirs to know.</summary>
        private async Task<(LayoutRecord Layout, LayoutResult Result)> FindEditableAsync(LayoutUser user, string id, CancellationToken cancellationToken)
        {
            var record = string.IsNullOrEmpty(id) ? null : await _store.FindAsync(id, cancellationToken).ConfigureAwait(false);

            if (record == null || (record.OwnerId != null && record.OwnerId != user.Id))
            {
                return (null, LayoutResult.NotFound());
            }

            return record.OwnerId == null && !user.IsAdmin ? (null, LayoutResult.Forbidden()) : (record, null);
        }

        private static List<string> Validate(LayoutRequest request)
        {
            var errors = new List<string>();

            if (request == null)
            {
                errors.Add("A layout is required.");
                return errors;
            }

            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            {
                errors.Add($"Name must be 1 to {MaxNameLength} characters.");
            }

            if (request.Grid == null || !ValidGrid().IsMatch(request.Grid))
            {
                errors.Add("Grid must be a layout name such as \"2x2\" or \"1+5\".");
            }

            var cameras = request.Cameras ?? new List<string>();
            if (cameras.Count > MaxTiles)
            {
                errors.Add($"A layout has at most {MaxTiles} tiles.");
            }

            // Cameras need not exist (they may be added later, or removed since); they must be valid ids.
            foreach (var camera in cameras.Where(c => !string.IsNullOrEmpty(c) && !CameraIdentifier.IsValid(c)).Distinct())
            {
                errors.Add($"'{camera}' is not a valid camera id.");
            }

            return errors;
        }

        private static void Apply(LayoutRecord record, LayoutRequest request, DateTime now)
        {
            record.Name = request.Name.Trim();
            record.Grid = request.Grid;
            record.Cameras = JsonSerializer.Serialize((request.Cameras ?? new List<string>()).Select(c => string.IsNullOrEmpty(c) ? null : c));
            record.UpdatedAt = now;
        }

        private static Layout ToLayout(LayoutRecord record, LayoutUser user)
        {
            return new Layout
            {
                Id = record.Id,
                Name = record.Name,
                Grid = record.Grid,
                Cameras = JsonSerializer.Deserialize<List<string>>(record.Cameras) ?? new List<string>(),
                Shared = record.OwnerId == null,
                Editable = record.OwnerId == user.Id || (record.OwnerId == null && user.IsAdmin),
                UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(record.UpdatedAt, DateTimeKind.Utc))
            };
        }

        #endregion
    }

    /// <summary>Who is asking: the signed-in user's id and whether they are an administrator.</summary>
    public readonly record struct LayoutUser(string Id, bool IsAdmin);

    public sealed class LayoutRequest
    {
        public string Name { get; set; }
        public string Grid { get; set; }

        /// <summary>The camera id of each tile, in the grid's order; null or empty for an empty tile.</summary>
        public List<string> Cameras { get; set; }

        /// <summary>Visible to every user (administrators only). Only read when the layout is created.</summary>
        public bool Shared { get; set; }
    }

    public sealed class Layout
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Grid { get; set; }
        public List<string> Cameras { get; set; }
        public bool Shared { get; set; }

        /// <summary>Whether the asking user may change or delete it.</summary>
        public bool Editable { get; set; }

        public DateTimeOffset UpdatedAt { get; set; }
    }

    public enum LayoutStatus
    {
        Ok,
        Invalid,
        NotFound,
        Forbidden
    }

    public sealed class LayoutResult
    {
        private LayoutResult(LayoutStatus status, Layout layout, IReadOnlyList<string> errors)
        {
            Status = status;
            Layout = layout;
            Errors = errors ?? Array.Empty<string>();
        }

        public LayoutStatus Status { get; }
        public Layout Layout { get; }
        public IReadOnlyList<string> Errors { get; }

        public static LayoutResult Ok(Layout layout) => new LayoutResult(LayoutStatus.Ok, layout, null);
        public static LayoutResult Invalid(IReadOnlyList<string> errors) => new LayoutResult(LayoutStatus.Invalid, null, errors);
        public static LayoutResult NotFound() => new LayoutResult(LayoutStatus.NotFound, null, null);
        public static LayoutResult Forbidden() => new LayoutResult(LayoutStatus.Forbidden, null, new[] { "Only administrators manage shared layouts." });
    }
}
