// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Audit
{
    /// <summary>
    /// Records who did what: requests only queue an entry, a background loop writes them in batches and removes entries
    /// older than the retention (Audit:RetentionDays, default 365). Viewing video is recorded at most once per user and
    /// camera within <see cref="ViewWindow"/>, so a player's many requests make one entry.
    /// </summary>
    public sealed class AuditLog : BackgroundService
    {
        public AuditLog(IAuditStore store, ILogger<AuditLog> logger, TimeProvider timeProvider = null, TimeSpan? retention = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _retention = retention ?? TimeSpan.FromDays(365);
        }

        public static readonly TimeSpan ViewWindow = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

        private readonly IAuditStore _store;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _retention;
        private readonly Channel<AuditRecord> _queue = Channel.CreateBounded<AuditRecord>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        private readonly ConcurrentDictionary<string, DateTimeOffset> _recentViews = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        /// <summary>Records an action by the signed-in user of <paramref name="http"/>.</summary>
        public void Record(HttpContext http, string action, string targetType = null, string targetId = null, string targetName = null, object details = null)
        {
            var user = http?.User;
            Record(new AuditRecord
            {
                OrganizationId = user?.FindFirstValue(RecorderClaims.Organization),
                UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
                UserName = user?.FindFirstValue(ClaimTypes.Name),
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                TargetName = targetName,
                Details = details == null ? null : JsonSerializer.Serialize(details),
                IpAddress = http?.Connection.RemoteIpAddress?.ToString()
            });
        }

        /// <summary>Records an action of a user who is not signed in yet (sign-in, setup).</summary>
        public void Record(HttpContext http, Member member, string organizationId, string userName, string action, object details = null)
        {
            Record(new AuditRecord
            {
                OrganizationId = member?.Membership.OrganizationId ?? organizationId,
                UserId = member?.User.Id,
                UserName = member?.User.UserName ?? Truncate(userName, 64),
                Action = action,
                Details = details == null ? null : JsonSerializer.Serialize(details),
                IpAddress = http?.Connection.RemoteIpAddress?.ToString()
            });
        }

        /// <summary>True when this user has not viewed this camera this way within <see cref="ViewWindow"/> (and marks it viewed).</summary>
        public bool IsNewView(ClaimsPrincipal user, string action, string cameraId)
        {
            var key = $"{user.FindFirstValue(ClaimTypes.NameIdentifier)}|{action}|{cameraId}";
            var now = _timeProvider.GetUtcNow();
            var isNew = !_recentViews.TryGetValue(key, out var last) || now - last >= ViewWindow;

            if (isNew)
            {
                _recentViews[key] = now;
            }

            return isNew;
        }

        private void Record(AuditRecord entry)
        {
            entry.Id = Guid.NewGuid().ToString("D");
            entry.CreatedAt = _timeProvider.GetUtcNow().UtcDateTime;
            entry.Details = entry.Details?.Length > 2000 ? null : entry.Details;
            entry.TargetName = Truncate(entry.TargetName, 200);
            _queue.Writer.TryWrite(entry);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var nextCleanup = _timeProvider.GetUtcNow();

            try
            {
                while (await _queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
                {
                    await FlushAsync(stoppingToken).ConfigureAwait(false);

                    if (_timeProvider.GetUtcNow() >= nextCleanup)
                    {
                        nextCleanup = _timeProvider.GetUtcNow() + CleanupInterval;
                        await CleanupAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                await FlushAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>Writes everything queued so far (also used by tests to wait for entries).</summary>
        public async Task FlushAsync(CancellationToken cancellationToken = default)
        {
            var batch = new List<AuditRecord>();
            while (_queue.Reader.TryRead(out var entry))
            {
                batch.Add(entry);
                if (batch.Count == 500)
                {
                    await WriteAsync(batch, cancellationToken).ConfigureAwait(false);
                    batch = new List<AuditRecord>();
                }
            }

            if (batch.Count > 0)
            {
                await WriteAsync(batch, cancellationToken).ConfigureAwait(false);
            }

            // Forget views older than the window.
            var now = _timeProvider.GetUtcNow();
            foreach (var view in _recentViews)
            {
                if (now - view.Value >= ViewWindow)
                {
                    _recentViews.TryRemove(view.Key, out _);
                }
            }
        }

        private async Task WriteAsync(List<AuditRecord> batch, CancellationToken cancellationToken)
        {
            try
            {
                await _store.AddAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Could not write {Count} audit entries", batch.Count);
            }
        }

        private async Task CleanupAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _store.DeleteBeforeAsync(_timeProvider.GetUtcNow().UtcDateTime - _retention, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogError(ex, "Could not remove old audit entries");
            }
        }

        private static string Truncate(string text, int length) => text == null || text.Length <= length ? text : text.Substring(0, length);
    }
}
