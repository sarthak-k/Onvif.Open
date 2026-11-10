// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Alerts
{
    /// <summary>
    /// Alert rules and the alerts they raised, in the settings database.
    /// </summary>
    public interface IAlertStore
    {
        /// <summary>An organization's rules by name; null: every organization's.</summary>
        Task<IReadOnlyList<AlertRule>> GetRulesAsync(string organizationId, CancellationToken cancellationToken = default);
        Task<AlertRule> FindRuleAsync(string id, CancellationToken cancellationToken = default);
        Task SaveRuleAsync(AlertRule rule, CancellationToken cancellationToken = default);
        Task DeleteRuleAsync(string id, CancellationToken cancellationToken = default);

        /// <summary>Newest first, at most <paramref name="limit"/>.</summary>
        Task<IReadOnlyList<AlertRecord>> GetAlertsAsync(string organizationId, bool openOnly, int limit, CancellationToken cancellationToken = default);
        Task<AlertRecord> FindAlertAsync(string id, CancellationToken cancellationToken = default);
        Task SaveAlertAsync(AlertRecord alert, CancellationToken cancellationToken = default);

        /// <summary>Removes alerts raised before <paramref name="cutoff"/>.</summary>
        Task<int> DeleteAlertsBeforeAsync(DateTime cutoff, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// <see cref="IAlertStore"/> on EF Core; webhook secrets are encrypted with Data Protection.
    /// </summary>
    public sealed class EfAlertStore : IAlertStore
    {
        public EfAlertStore(IDbContextFactory<RecorderDbContext> contexts, IDataProtectionProvider dataProtection)
        {
            _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
            _secrets = (dataProtection ?? throw new ArgumentNullException(nameof(dataProtection))).CreateProtector("Onvif.Open.Recorder.AlertWebhookSecrets");
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
        private readonly IDbContextFactory<RecorderDbContext> _contexts;
        private readonly IDataProtector _secrets;

        public async Task<IReadOnlyList<AlertRule>> GetRulesAsync(string organizationId, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var rules = db.AlertRules.AsNoTracking();
                if (organizationId != null)
                {
                    rules = rules.Where(r => r.OrganizationId == organizationId);
                }

                return (await rules.ToListAsync(cancellationToken).ConfigureAwait(false)).Select(ToRule).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        public async Task<AlertRule> FindRuleAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = await db.AlertRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
                return record == null ? null : ToRule(record);
            }
        }

        public async Task SaveRuleAsync(AlertRule rule, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var record = await db.AlertRules.FirstOrDefaultAsync(r => r.Id == rule.Id, cancellationToken).ConfigureAwait(false);
                if (record == null)
                {
                    record = new AlertRuleRecord { Id = rule.Id };
                    db.AlertRules.Add(record);
                }

                record.OrganizationId = rule.OrganizationId;
                record.Name = rule.Name;
                record.Enabled = rule.Enabled;
                record.Kind = rule.Kind;
                record.Settings = JsonSerializer.Serialize(new Settings
                {
                    Categories = rule.Categories,
                    MinSeverity = rule.MinSeverity,
                    OfflineMinutes = rule.OfflineMinutes,
                    SiteIds = rule.SiteIds,
                    CameraIds = rule.CameraIds,
                    Hours = rule.Hours,
                    CooldownMinutes = rule.CooldownMinutes,
                    Emails = rule.Emails
                }, Json);
                record.WebhookUrl = rule.WebhookUrl;
                record.WebhookSecret = string.IsNullOrEmpty(rule.WebhookSecret) ? null : _secrets.Protect(rule.WebhookSecret);
                record.CreatedAt = rule.CreatedAt;
                record.UpdatedAt = rule.UpdatedAt;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task DeleteRuleAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                await db.AlertRules.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<IReadOnlyList<AlertRecord>> GetAlertsAsync(string organizationId, bool openOnly, int limit, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var alerts = db.Alerts.AsNoTracking().Where(a => a.OrganizationId == organizationId);
                if (openOnly)
                {
                    alerts = alerts.Where(a => a.AcknowledgedAt == null);
                }

                return await alerts.OrderByDescending(a => a.TriggeredAt).Take(limit).ToListAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<AlertRecord> FindAlertAsync(string id, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Alerts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task SaveAlertAsync(AlertRecord alert, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await db.Alerts.AnyAsync(a => a.Id == alert.Id, cancellationToken).ConfigureAwait(false))
                {
                    db.Alerts.Update(alert);
                }
                else
                {
                    db.Alerts.Add(alert);
                }

                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<int> DeleteAlertsBeforeAsync(DateTime cutoff, CancellationToken cancellationToken = default)
        {
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await db.Alerts.Where(a => a.TriggeredAt < cutoff).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        private AlertRule ToRule(AlertRuleRecord record)
        {
            var settings = string.IsNullOrEmpty(record.Settings) ? new Settings() : JsonSerializer.Deserialize<Settings>(record.Settings, Json) ?? new Settings();
            string secret = null;
            if (!string.IsNullOrEmpty(record.WebhookSecret))
            {
                try
                {
                    secret = _secrets.Unprotect(record.WebhookSecret);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    // The keys were lost: the rule posts unsigned until the secret is set again.
                }
            }

            return new AlertRule
            {
                Id = record.Id,
                OrganizationId = record.OrganizationId,
                Name = record.Name,
                Enabled = record.Enabled,
                Kind = record.Kind,
                Categories = settings.Categories ?? new List<string>(),
                MinSeverity = settings.MinSeverity,
                OfflineMinutes = settings.OfflineMinutes,
                SiteIds = settings.SiteIds ?? new List<string>(),
                CameraIds = settings.CameraIds ?? new List<string>(),
                Hours = settings.Hours ?? new List<ScheduleWindow>(),
                CooldownMinutes = settings.CooldownMinutes,
                Emails = settings.Emails ?? new List<string>(),
                WebhookUrl = record.WebhookUrl,
                WebhookSecret = secret,
                CreatedAt = DateTime.SpecifyKind(record.CreatedAt, DateTimeKind.Utc),
                UpdatedAt = DateTime.SpecifyKind(record.UpdatedAt, DateTimeKind.Utc)
            };
        }

        private sealed class Settings
        {
            public List<string> Categories { get; set; }
            public string MinSeverity { get; set; }
            public int OfflineMinutes { get; set; } = 5;
            public List<string> SiteIds { get; set; }
            public List<string> CameraIds { get; set; }
            public List<ScheduleWindow> Hours { get; set; }
            public int CooldownMinutes { get; set; } = 5;
            public List<string> Emails { get; set; }
        }
    }
}
