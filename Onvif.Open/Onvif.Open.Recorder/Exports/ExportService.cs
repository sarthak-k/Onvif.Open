// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Recorder.Gateways;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;
using Onvif.Open.Recording.Implementation.Export;

namespace Onvif.Open.Recorder.Exports
{
    public sealed class ExportRequest
    {
        public string CameraId { get; set; }
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
        public string CaseId { get; set; }
    }

    /// <summary>A share link as created: the token is in the URL and shown only now.</summary>
    public sealed record CreatedLink(ShareLinkRecord Link, string Token);

    /// <summary>
    /// MP4 exports: a stretch of one camera's recordings, cut, encoded with a watermark (organization, site, camera,
    /// who exported it, and the site time of every frame) and kept for <see cref="Lifetime"/>, with its SHA-256. Exports
    /// run one at a time in the background (and resume after a restart). Share links let anyone holding one download an
    /// export until the link expires or is revoked.
    /// </summary>
    public sealed class ExportService : BackgroundService
    {
        #region Constructor

        public ExportService(IExportStore store, IRecordingManager recording, CameraDirectory directory, ITenancyStore tenancy, IMp4Encoder encoder, string directoryPath, ILogger<ExportService> logger, TimeProvider timeProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _recording = recording ?? throw new ArgumentNullException(nameof(recording));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _tenancy = tenancy ?? throw new ArgumentNullException(nameof(tenancy));
            _encoder = encoder ?? throw new ArgumentNullException(nameof(encoder));
            Folder = Path.GetFullPath(directoryPath ?? throw new ArgumentNullException(nameof(directoryPath)));
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Public Variables

        /// <summary>How long an export can be (it is encoded again, frame by frame).</summary>
        public static readonly TimeSpan MaxLength = TimeSpan.FromHours(1);

        /// <summary>How long an export's file is kept.</summary>
        public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

        public static readonly TimeSpan MaxLinkLifetime = TimeSpan.FromDays(7);
        public const int MaxWaiting = 20;

        public string Folder { get; }

        #endregion

        #region Private Variables

        private readonly IExportStore _store;
        private readonly IRecordingManager _recording;
        private readonly CameraDirectory _directory;
        private readonly ITenancyStore _tenancy;
        private readonly IMp4Encoder _encoder;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
        private int _waiting;

        #endregion

        #region Public Methods

        public async Task<ServiceResult<ExportRecord>> CreateAsync(ClaimsPrincipal user, ExportRequest request, CancellationToken cancellationToken = default)
        {
            var camera = _directory.Find(request?.CameraId);
            if (camera == null || !user.CanSee(camera))
            {
                return ServiceResult<ExportRecord>.NotFound();
            }

            var errors = new List<string>();
            if (request.To <= request.From || request.To - request.From > MaxLength)
            {
                errors.Add($"An export ends after it starts and lasts at most {MaxLength.TotalMinutes:0} minutes.");
            }
            else if (!_recording.GetSegments(camera.Id).Any(s => s.StartTime <= request.To && s.LastWriteTime >= request.From))
            {
                errors.Add("Nothing was recorded in that time.");
            }

            if (errors.Count > 0)
            {
                return ServiceResult<ExportRecord>.Invalid(errors);
            }

            if (Volatile.Read(ref _waiting) >= MaxWaiting)
            {
                return ServiceResult<ExportRecord>.Conflict("Too many exports are waiting: try again when they are done.");
            }

            var now = Now;
            var export = new ExportRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                OrganizationId = camera.OrganizationId,
                SiteId = camera.SiteId,
                CameraId = camera.Id,
                CameraName = camera.Name,
                From = request.From.UtcDateTime,
                To = request.To.UtcDateTime,
                CaseId = string.IsNullOrWhiteSpace(request.CaseId) ? null : request.CaseId.Trim(),
                RequestedById = user.FindFirstValue(ClaimTypes.NameIdentifier),
                RequestedByName = user.Identity?.Name,
                Status = ExportStatuses.Queued,
                CreatedAt = now,
                ExpiresAt = now + Lifetime
            };
            await _store.SaveAsync(export, cancellationToken).ConfigureAwait(false);
            Enqueue(export.Id);
            return ServiceResult<ExportRecord>.Ok(export);
        }

        /// <summary>The organization's exports the user may see (their sites), newest first.</summary>
        public async Task<IReadOnlyList<ExportRecord>> GetAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            (await _store.GetAsync(user.OrganizationId(), 200, cancellationToken).ConfigureAwait(false)).Where(e => user.CanSeeSite(e.OrganizationId, e.SiteId)).ToList();

        public async Task<ExportRecord> FindAsync(ClaimsPrincipal user, string id, CancellationToken cancellationToken = default)
        {
            var export = await _store.FindAsync(id, cancellationToken).ConfigureAwait(false);
            return export != null && user.CanSeeSite(export.OrganizationId, export.SiteId) ? export : null;
        }

        /// <summary>The file of a ready export, or null.</summary>
        public string FileOf(ExportRecord export) =>
            export?.Status == ExportStatuses.Ready && export.ExpiresAt > Now && File.Exists(PathOf(export.Id)) ? PathOf(export.Id) : null;

        public static string FileName(ExportRecord export) =>
            string.Format(CultureInfo.InvariantCulture, "{0}_{1:yyyyMMdd'T'HHmmss'Z'}.mp4", Safe(export.CameraName ?? export.CameraId), export.From);

        public Task<IReadOnlyList<ShareLinkRecord>> GetLinksAsync(ExportRecord export, CancellationToken cancellationToken = default) =>
            _store.GetLinksAsync(export.Id, cancellationToken);

        public async Task<ServiceResult<CreatedLink>> CreateLinkAsync(ClaimsPrincipal user, ExportRecord export, TimeSpan lifetime, CancellationToken cancellationToken = default)
        {
            if (FileOf(export) == null)
            {
                return ServiceResult<CreatedLink>.Conflict("Only a finished export can be shared.");
            }

            if (lifetime <= TimeSpan.Zero || lifetime > MaxLinkLifetime)
            {
                return ServiceResult<CreatedLink>.Invalid(new[] { $"A link lasts 1 hour to {MaxLinkLifetime.TotalDays:0} days." });
            }

            var token = GatewayTokens.Create();
            var now = Now;
            var expires = now + lifetime;
            var link = new ShareLinkRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                ExportId = export.Id,
                OrganizationId = export.OrganizationId,
                TokenHash = GatewayTokens.Hash(token),
                CreatedByName = user.Identity?.Name,
                CreatedAt = now,
                ExpiresAt = expires < export.ExpiresAt ? expires : export.ExpiresAt
            };
            await _store.SaveLinkAsync(link, cancellationToken).ConfigureAwait(false);
            return ServiceResult<CreatedLink>.Ok(new CreatedLink(link, token));
        }

        public async Task<ServiceResult<ShareLinkRecord>> RevokeLinkAsync(ExportRecord export, string linkId, CancellationToken cancellationToken = default)
        {
            var link = await _store.FindLinkAsync(linkId, cancellationToken).ConfigureAwait(false);
            if (link == null || link.ExportId != export.Id)
            {
                return ServiceResult<ShareLinkRecord>.NotFound();
            }

            link.RevokedAt ??= Now;
            await _store.SaveLinkAsync(link, cancellationToken).ConfigureAwait(false);
            return ServiceResult<ShareLinkRecord>.Ok(link);
        }

        /// <summary>The export a share link opens, counting the download; null when the link is unknown, revoked or expired.</summary>
        public async Task<(ExportRecord Export, ShareLinkRecord Link)> OpenSharedAsync(string token, CancellationToken cancellationToken = default)
        {
            var link = string.IsNullOrWhiteSpace(token) ? null : await _store.FindLinkByTokenHashAsync(GatewayTokens.Hash(token.Trim()), cancellationToken).ConfigureAwait(false);
            if (link == null || link.RevokedAt != null || link.ExpiresAt <= Now)
            {
                return (null, null);
            }

            var export = await _store.FindAsync(link.ExportId, cancellationToken).ConfigureAwait(false);
            if (FileOf(export) == null)
            {
                return (null, null);
            }

            link.Downloads++;
            await _store.SaveLinkAsync(link, cancellationToken).ConfigureAwait(false);
            return (export, link);
        }

        /// <summary>Runs one export now (the background loop does this; also used by tests).</summary>
        public async Task RunAsync(string id, CancellationToken cancellationToken = default)
        {
            var export = await _store.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (export == null || !ExportStatuses.IsUnfinished(export.Status))
            {
                return;
            }

            export.Status = ExportStatuses.Machine.Move(export.Status, ExportStatuses.Running);
            await _store.SaveAsync(export, cancellationToken).ConfigureAwait(false);

            Directory.CreateDirectory(Folder);
            var clip = Path.Combine(Folder, export.Id + ".ts");
            var output = PathOf(export.Id);

            try
            {
                await using (var file = File.Create(clip))
                {
                    await ClipExport.WriteAsync(_recording, export.CameraId, new DateTimeOffset(export.From, TimeSpan.Zero), new DateTimeOffset(export.To, TimeSpan.Zero), file, cancellationToken).ConfigureAwait(false);
                }

                await _encoder.EncodeAsync(clip, output, await WatermarkAsync(export, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

                await using (var file = File.OpenRead(output))
                {
                    export.Sha256 = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
                    export.Size = file.Length;
                }

                export.Status = ExportStatuses.Machine.Move(export.Status, ExportStatuses.Ready);
                _logger?.LogInformation("Export {Id} of camera {CameraId} ready ({Size} bytes)", export.Id, export.CameraId, export.Size);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                export.Status = ExportStatuses.Machine.Move(export.Status, ExportStatuses.Failed);
                export.Error = ex is InvalidOperationException || ex is IOException || ex is KeyNotFoundException ? ex.Message : "The export failed.";
                _logger?.LogWarning(ex, "Export {Id} of camera {CameraId} failed", export.Id, export.CameraId);
                TryDelete(output);
            }
            finally
            {
                TryDelete(clip);
            }

            export.CompletedAt = Now;
            await _store.SaveAsync(export, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>Deletes the files of exports past their time.</summary>
        public async Task CleanupAsync(CancellationToken cancellationToken = default)
        {
            foreach (var export in await _store.GetExpiredAsync(Now, cancellationToken).ConfigureAwait(false))
            {
                TryDelete(PathOf(export.Id));
                export.Status = ExportStatuses.Machine.Move(export.Status, ExportStatuses.Expired);
                await _store.SaveAsync(export, cancellationToken).ConfigureAwait(false);
            }
        }

        #endregion

        #region Protected Methods

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Exports interrupted by a restart run again.
            foreach (var export in await _store.GetUnfinishedAsync(stoppingToken).ConfigureAwait(false))
            {
                Enqueue(export.Id);
            }

            var nextCleanup = DateTimeOffset.MinValue;
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var tick = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                {
                    tick.CancelAfter(TimeSpan.FromMinutes(10));
                    try
                    {
                        if (await _queue.Reader.WaitToReadAsync(tick.Token).ConfigureAwait(false) && _queue.Reader.TryRead(out var id))
                        {
                            Interlocked.Decrement(ref _waiting);
                            await RunAsync(id, stoppingToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Ten quiet minutes: time for the cleanup.
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger?.LogError(ex, "The export loop failed");
                    }
                }

                if (_timeProvider.GetUtcNow() >= nextCleanup)
                {
                    nextCleanup = _timeProvider.GetUtcNow().AddHours(1);
                    try
                    {
                        await CleanupAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        _logger?.LogWarning(ex, "Could not remove expired exports");
                    }
                }
            }
        }

        #endregion

        #region Private Methods

        private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

        private string PathOf(string id) => Path.Combine(Folder, id + ".mp4");

        private void Enqueue(string id)
        {
            Interlocked.Increment(ref _waiting);
            _queue.Writer.TryWrite(id);
        }

        private async Task<Watermark> WatermarkAsync(ExportRecord export, CancellationToken cancellationToken)
        {
            var site = await _tenancy.FindSiteAsync(export.SiteId, cancellationToken).ConfigureAwait(false);
            var organization = await _tenancy.FindOrganizationAsync(export.OrganizationId, cancellationToken).ConfigureAwait(false);
            var zone = FindZone(site?.TimeZone);
            var start = new DateTimeOffset(export.From, TimeSpan.Zero);
            var offset = zone.GetUtcOffset(start);
            var exported = TimeZoneInfo.ConvertTime(new DateTimeOffset(export.CreatedAt, TimeSpan.Zero), zone);

            return new Watermark(
                new[]
                {
                    $"Onvif.Open · {organization?.Name} · {site?.Name} · {export.CameraName ?? export.CameraId}",
                    $"Exported by {export.RequestedByName ?? "unknown"} on {exported:yyyy-MM-dd HH:mm} · ref {export.Id.Substring(0, 8)}"
                },
                start,
                offset,
                "UTC" + (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString(@"hh\:mm", CultureInfo.InvariantCulture));
        }

        private static TimeZoneInfo FindZone(string id)
        {
            try
            {
                return string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }

        private static string Safe(string name) =>
            new string(name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray()).Trim('-') is { Length: > 0 } safe ? safe : "camera";

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion
    }
}
