// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Cases
{
    public sealed class CaseRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>"Open" or "Closed"; null keeps it.</summary>
        public string Status { get; set; }
    }

    public sealed class ClipRequest
    {
        public List<string> CameraIds { get; set; } = new List<string>();
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
        public string Note { get; set; }
        public string EventId { get; set; }
    }

    public sealed class NoteRequest
    {
        public string Note { get; set; }
    }

    /// <summary>A case as a user sees it: only the clips of sites they may see.</summary>
    public sealed record CaseView(CaseRecord Case, IReadOnlyList<CaseClipRecord> Clips, int ClipCount);

    /// <summary>
    /// Cases: incidents being looked into, each with clips (bookmarks) of video and notes. Everyone sees the
    /// organization's cases, with the clips of their own sites; operators and above open cases and add clips;
    /// administrators delete cases. The video of open cases is kept (see <see cref="CaseHolds"/>).
    /// </summary>
    public sealed class CaseService
    {
        #region Constructor

        public CaseService(ICaseStore store, CaseHolds holds, CameraDirectory directory, TimeProvider timeProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _holds = holds ?? throw new ArgumentNullException(nameof(holds));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Public Variables

        public const int MaxNameLength = 200;
        public const int MaxTextLength = 4000;
        public const int MaxCamerasPerClip = 4;
        public static readonly TimeSpan MaxClipLength = TimeSpan.FromHours(6);

        #endregion

        #region Private Variables

        private readonly ICaseStore _store;
        private readonly CaseHolds _holds;
        private readonly CameraDirectory _directory;
        private readonly TimeProvider _timeProvider;

        #endregion

        #region Public Methods

        public static bool CanEdit(ClaimsPrincipal user) => Roles.AtLeast(Roles.Operator).Any(user.IsInRole);

        public async Task<IReadOnlyList<(CaseRecord Case, int Clips)>> GetAsync(ClaimsPrincipal user, string status, CancellationToken cancellationToken = default) =>
            await _store.GetAsync(user.OrganizationId(), CaseStatuses.Normalize(status), cancellationToken).ConfigureAwait(false);

        public async Task<CaseView> FindAsync(ClaimsPrincipal user, string id, CancellationToken cancellationToken = default)
        {
            var record = await _store.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (record == null || record.OrganizationId != user.OrganizationId())
            {
                return null;
            }

            var clips = await _store.GetClipsAsync(record.Id, cancellationToken).ConfigureAwait(false);
            return new CaseView(record, clips.Where(c => user.CanSeeSite(c.OrganizationId, c.SiteId)).ToList(), clips.Count);
        }

        public async Task<ServiceResult<CaseRecord>> CreateAsync(ClaimsPrincipal user, CaseRequest request, CancellationToken cancellationToken = default)
        {
            var errors = Check(request, requireName: true);
            if (errors.Count > 0)
            {
                return ServiceResult<CaseRecord>.Invalid(errors);
            }

            var now = Now;
            var record = new CaseRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                OrganizationId = user.OrganizationId(),
                Name = request.Name.Trim(),
                Description = Text(request.Description),
                Status = CaseStatuses.Open,
                CreatedById = user.FindFirstValue(ClaimTypes.NameIdentifier),
                CreatedByName = user.Identity?.Name,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);
            return ServiceResult<CaseRecord>.Ok(record);
        }

        public async Task<ServiceResult<CaseRecord>> UpdateAsync(ClaimsPrincipal user, string id, CaseRequest request, CancellationToken cancellationToken = default)
        {
            var record = await OwnAsync(user, id, cancellationToken).ConfigureAwait(false);
            if (record == null)
            {
                return ServiceResult<CaseRecord>.NotFound();
            }

            var errors = Check(request, requireName: false);
            var status = request?.Status == null ? record.Status : CaseStatuses.Normalize(request.Status);
            if (status == null)
            {
                errors.Add("Status is Open or Closed.");
            }

            if (errors.Count > 0)
            {
                return ServiceResult<CaseRecord>.Invalid(errors);
            }

            var now = Now;
            record.Name = request.Name == null ? record.Name : request.Name.Trim();
            record.Description = request.Description == null ? record.Description : Text(request.Description);
            record.ClosedAt = status == CaseStatuses.Closed ? (record.Status == CaseStatuses.Closed ? record.ClosedAt : now) : null;
            var changedStatus = record.Status != status;
            record.Status = changedStatus ? CaseStatuses.Machine.Move(record.Status, status) : status;
            record.UpdatedAt = now;
            await _store.SaveAsync(record, cancellationToken).ConfigureAwait(false);

            if (changedStatus)
            {
                await _holds.ReloadAsync(cancellationToken).ConfigureAwait(false);
            }

            return ServiceResult<CaseRecord>.Ok(record);
        }

        public async Task<ServiceResult<CaseRecord>> DeleteAsync(ClaimsPrincipal user, string id, CancellationToken cancellationToken = default)
        {
            var record = await OwnAsync(user, id, cancellationToken).ConfigureAwait(false);
            if (record == null)
            {
                return ServiceResult<CaseRecord>.NotFound();
            }

            await _store.DeleteAsync(record.Id, cancellationToken).ConfigureAwait(false);
            await _holds.ReloadAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResult<CaseRecord>.Ok(record);
        }

        public async Task<ServiceResult<CaseClipRecord>> AddClipAsync(ClaimsPrincipal user, string caseId, ClipRequest request, CancellationToken cancellationToken = default)
        {
            var record = await OwnAsync(user, caseId, cancellationToken).ConfigureAwait(false);
            if (record == null)
            {
                return ServiceResult<CaseClipRecord>.NotFound();
            }

            if (record.Status != CaseStatuses.Open)
            {
                return ServiceResult<CaseClipRecord>.Conflict($"Case '{record.Name}' is closed: reopen it to add clips.");
            }

            var errors = new List<string>();
            var cameras = (request?.CameraIds ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var entries = cameras.Select(c => _directory.Find(c)).ToList();

            if (cameras.Count == 0 || cameras.Count > MaxCamerasPerClip)
            {
                errors.Add($"A clip has 1 to {MaxCamerasPerClip} cameras.");
            }
            else if (entries.Any(e => e == null || !user.CanSee(e)))
            {
                errors.Add("Unknown camera.");
            }
            else if (entries.Select(e => e.SiteId).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                errors.Add("The cameras of a clip are at one site.");
            }

            if (request == null || request.To <= request.From)
            {
                errors.Add("A clip ends after it starts.");
            }
            else if (request.To - request.From > MaxClipLength)
            {
                errors.Add($"A clip is at most {MaxClipLength.TotalHours:0} hours long.");
            }
            else if (request.From > _timeProvider.GetUtcNow())
            {
                errors.Add("A clip cannot start in the future.");
            }

            if (request?.Note?.Length > MaxTextLength)
            {
                errors.Add($"A note is at most {MaxTextLength} characters.");
            }

            if (errors.Count > 0)
            {
                return ServiceResult<CaseClipRecord>.Invalid(errors);
            }

            var now = Now;
            var clip = new CaseClipRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                CaseId = record.Id,
                OrganizationId = record.OrganizationId,
                SiteId = entries[0].SiteId,
                Cameras = JsonSerializer.Serialize(entries.Select(e => e.Id).ToList()),
                From = request.From.UtcDateTime,
                To = request.To.UtcDateTime,
                Note = Text(request.Note),
                EventId = string.IsNullOrWhiteSpace(request.EventId) ? null : request.EventId.Trim(),
                CreatedById = user.FindFirstValue(ClaimTypes.NameIdentifier),
                CreatedByName = user.Identity?.Name,
                CreatedAt = now
            };
            await _store.SaveClipAsync(clip, cancellationToken).ConfigureAwait(false);
            await Touch(record, now, cancellationToken).ConfigureAwait(false);
            await _holds.ReloadAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResult<CaseClipRecord>.Ok(clip);
        }

        public async Task<ServiceResult<CaseClipRecord>> UpdateNoteAsync(ClaimsPrincipal user, string caseId, string clipId, string note, CancellationToken cancellationToken = default)
        {
            var (record, clip) = await OwnClipAsync(user, caseId, clipId, cancellationToken).ConfigureAwait(false);
            if (clip == null)
            {
                return ServiceResult<CaseClipRecord>.NotFound();
            }

            if (note?.Length > MaxTextLength)
            {
                return ServiceResult<CaseClipRecord>.Invalid(new[] { $"A note is at most {MaxTextLength} characters." });
            }

            clip.Note = Text(note);
            await _store.SaveClipAsync(clip, cancellationToken).ConfigureAwait(false);
            await Touch(record, Now, cancellationToken).ConfigureAwait(false);
            return ServiceResult<CaseClipRecord>.Ok(clip);
        }

        public async Task<ServiceResult<CaseClipRecord>> RemoveClipAsync(ClaimsPrincipal user, string caseId, string clipId, CancellationToken cancellationToken = default)
        {
            var (record, clip) = await OwnClipAsync(user, caseId, clipId, cancellationToken).ConfigureAwait(false);
            if (clip == null)
            {
                return ServiceResult<CaseClipRecord>.NotFound();
            }

            await _store.DeleteClipAsync(clip.Id, cancellationToken).ConfigureAwait(false);
            await Touch(record, Now, cancellationToken).ConfigureAwait(false);
            await _holds.ReloadAsync(cancellationToken).ConfigureAwait(false);
            return ServiceResult<CaseClipRecord>.Ok(clip);
        }

        #endregion

        #region Private Methods

        private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

        private async Task<CaseRecord> OwnAsync(ClaimsPrincipal user, string id, CancellationToken cancellationToken)
        {
            var record = await _store.FindAsync(id, cancellationToken).ConfigureAwait(false);
            return record?.OrganizationId == user.OrganizationId() ? record : null;
        }

        /// <summary>The clip when it is in the case and the user may see its site.</summary>
        private async Task<(CaseRecord Case, CaseClipRecord Clip)> OwnClipAsync(ClaimsPrincipal user, string caseId, string clipId, CancellationToken cancellationToken)
        {
            var record = await OwnAsync(user, caseId, cancellationToken).ConfigureAwait(false);
            var clip = record == null ? null : await _store.FindClipAsync(clipId, cancellationToken).ConfigureAwait(false);
            return clip != null && clip.CaseId == record.Id && user.CanSeeSite(clip.OrganizationId, clip.SiteId) ? (record, clip) : (record, null);
        }

        private Task Touch(CaseRecord record, DateTime now, CancellationToken cancellationToken)
        {
            record.UpdatedAt = now;
            return _store.SaveAsync(record, cancellationToken);
        }

        private static List<string> Check(CaseRequest request, bool requireName)
        {
            var errors = new List<string>();
            if (request == null)
            {
                errors.Add("A case is required.");
                return errors;
            }

            if ((requireName || request.Name != null) && (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > MaxNameLength))
            {
                errors.Add($"Name is required and at most {MaxNameLength} characters.");
            }

            if (request.Description?.Length > MaxTextLength)
            {
                errors.Add($"The description is at most {MaxTextLength} characters.");
            }

            return errors;
        }

        private static string Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        #endregion
    }
}
