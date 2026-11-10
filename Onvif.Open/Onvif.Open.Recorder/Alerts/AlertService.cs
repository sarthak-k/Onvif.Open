// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Alerts
{
    /// <summary>A rule as it is created or changed: every field is replaced, except a null webhook secret, which keeps it.</summary>
    public sealed class AlertRuleRequest
    {
        public string Name { get; set; }
        public bool? Enabled { get; set; }
        public string Kind { get; set; }
        public List<string> Categories { get; set; }
        public string MinSeverity { get; set; }
        public int? OfflineMinutes { get; set; }
        public List<string> SiteIds { get; set; }
        public List<string> CameraIds { get; set; }
        public List<ScheduleWindow> Hours { get; set; }
        public int? CooldownMinutes { get; set; }
        public List<string> Emails { get; set; }
        public string WebhookUrl { get; set; }

        /// <summary>Null keeps it; empty removes it.</summary>
        public string WebhookSecret { get; set; }
    }

    /// <summary>The open alerts a user sees, and the newest of them (for desktop notifications).</summary>
    public sealed record AlertSummary(int Open, AlertRecord Latest);

    /// <summary>
    /// Alert rules (organization administrators set them up) and the alerts inbox: everyone sees the alerts of their
    /// sites; operators and above acknowledge them.
    /// </summary>
    public sealed class AlertService
    {
        #region Constructor

        public AlertService(IAlertStore store, AlertRuleCache rules, AlertDelivery delivery, ITenancyStore tenancy, CameraDirectory directory, TimeProvider timeProvider = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            _delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
            _tenancy = tenancy ?? throw new ArgumentNullException(nameof(tenancy));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Public Variables

        public const int MaxRules = 50;
        public const int MaxNameLength = 100;
        public const int MaxEmails = 10;
        public const int MaxMinutes = 24 * 60;
        public const int MaxAlerts = 200;

        #endregion

        #region Private Variables

        private readonly IAlertStore _store;
        private readonly AlertRuleCache _rules;
        private readonly AlertDelivery _delivery;
        private readonly ITenancyStore _tenancy;
        private readonly CameraDirectory _directory;
        private readonly TimeProvider _timeProvider;

        #endregion

        #region Public Methods

        public Task<IReadOnlyList<AlertRule>> GetRulesAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            _store.GetRulesAsync(user.OrganizationId(), cancellationToken);

        public async Task<AlertRule> FindRuleAsync(ClaimsPrincipal user, string id, CancellationToken cancellationToken = default)
        {
            var rule = await _store.FindRuleAsync(id, cancellationToken).ConfigureAwait(false);
            return rule != null && rule.OrganizationId == user.OrganizationId() ? rule : null;
        }

        public async Task<ServiceResult<AlertRule>> CreateRuleAsync(ClaimsPrincipal user, AlertRuleRequest request, CancellationToken cancellationToken = default)
        {
            var organization = user.OrganizationId();
            if ((await _store.GetRulesAsync(organization, cancellationToken).ConfigureAwait(false)).Count >= MaxRules)
            {
                return ServiceResult<AlertRule>.Conflict($"An organization has at most {MaxRules} alert rules.");
            }

            var now = Now;
            var rule = new AlertRule { Id = Guid.NewGuid().ToString("D"), OrganizationId = organization, CreatedAt = now };
            return await SaveAsync(rule, request, now, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ServiceResult<AlertRule>> UpdateRuleAsync(ClaimsPrincipal user, string id, AlertRuleRequest request, CancellationToken cancellationToken = default)
        {
            var rule = await FindRuleAsync(user, id, cancellationToken).ConfigureAwait(false);
            return rule == null ? ServiceResult<AlertRule>.NotFound() : await SaveAsync(rule, request, Now, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ServiceResult<AlertRule>> DeleteRuleAsync(ClaimsPrincipal user, string id, CancellationToken cancellationToken = default)
        {
            var rule = await FindRuleAsync(user, id, cancellationToken).ConfigureAwait(false);
            if (rule == null)
            {
                return ServiceResult<AlertRule>.NotFound();
            }

            await _store.DeleteRuleAsync(id, cancellationToken).ConfigureAwait(false);
            _rules.Invalidate();
            return ServiceResult<AlertRule>.Ok(rule);
        }

        /// <summary>Sends a test alert the way the rule would, without saving it.</summary>
        public async Task<ServiceResult<IReadOnlyDictionary<string, DeliveryResult>>> TestRuleAsync(ClaimsPrincipal user, string id, CancellationToken cancellationToken = default)
        {
            var rule = await FindRuleAsync(user, id, cancellationToken).ConfigureAwait(false);
            if (rule == null)
            {
                return ServiceResult<IReadOnlyDictionary<string, DeliveryResult>>.NotFound();
            }

            var camera = rule.CameraIds.Select(_directory.Find).FirstOrDefault(c => c != null);
            var alert = new AlertRecord
            {
                Id = Guid.NewGuid().ToString("D"),
                OrganizationId = rule.OrganizationId,
                RuleId = rule.Id,
                RuleName = rule.Name,
                SiteId = camera?.SiteId ?? rule.SiteIds.FirstOrDefault(),
                CameraId = camera?.Id,
                CameraName = camera?.Name,
                Category = "test",
                Severity = Severities.Info,
                Title = "Test alert",
                TriggeredAt = Now
            };
            return ServiceResult<IReadOnlyDictionary<string, DeliveryResult>>.Ok(await _delivery.DeliverAsync(rule, alert, cancellationToken).ConfigureAwait(false));
        }

        /// <summary>Newest first; the alerts of the sites the user sees.</summary>
        public async Task<IReadOnlyList<AlertRecord>> GetAlertsAsync(ClaimsPrincipal user, bool openOnly, CancellationToken cancellationToken = default) =>
            (await _store.GetAlertsAsync(user.OrganizationId(), openOnly, MaxAlerts, cancellationToken).ConfigureAwait(false))
                .Where(a => user.CanSeeSite(a.OrganizationId, a.SiteId))
                .ToList();

        public async Task<AlertSummary> SummaryAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
        {
            var open = await GetAlertsAsync(user, true, cancellationToken).ConfigureAwait(false);
            return new AlertSummary(open.Count, open.FirstOrDefault());
        }

        public async Task<ServiceResult<AlertRecord>> AcknowledgeAsync(ClaimsPrincipal user, string id, CancellationToken cancellationToken = default)
        {
            var alert = await _store.FindAlertAsync(id, cancellationToken).ConfigureAwait(false);
            if (alert == null || !user.CanSeeSite(alert.OrganizationId, alert.SiteId))
            {
                return ServiceResult<AlertRecord>.NotFound();
            }

            // Acknowledging twice keeps the first acknowledgement.
            if (AlertStates.Machine.CanMove(AlertStates.Of(alert), AlertStates.Acknowledged))
            {
                alert.AcknowledgedAt = Now;
                alert.AcknowledgedBy = user.Identity?.Name;
                await _store.SaveAlertAsync(alert, cancellationToken).ConfigureAwait(false);
            }

            return ServiceResult<AlertRecord>.Ok(alert);
        }

        #endregion

        #region Private Methods

        private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

        private async Task<ServiceResult<AlertRule>> SaveAsync(AlertRule rule, AlertRuleRequest request, DateTime now, CancellationToken cancellationToken)
        {
            var errors = new List<string>();
            if (request == null)
            {
                return ServiceResult<AlertRule>.Invalid(new[] { "The rule is missing." });
            }

            var name = request.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            {
                errors.Add($"A rule needs a name of at most {MaxNameLength} characters.");
            }

            var kind = AlertKinds.Normalize(request.Kind ?? AlertKinds.Event);
            if (kind == null)
            {
                errors.Add("A rule is about an Event or a camera being Offline.");
            }

            var categories = Distinct(request.Categories).Select(c => c.ToLowerInvariant()).ToList();
            if (categories.Any(c => !EventCategories.All.Contains(c)))
            {
                errors.Add($"Categories are {string.Join(", ", EventCategories.All)}.");
            }

            var severity = string.IsNullOrWhiteSpace(request.MinSeverity) ? null : Severities.Normalize(request.MinSeverity);
            if (!string.IsNullOrWhiteSpace(request.MinSeverity) && severity == null)
            {
                errors.Add("The least severity is Info, Warning or Critical.");
            }

            var offlineMinutes = request.OfflineMinutes ?? 5;
            var cooldown = request.CooldownMinutes ?? 5;
            if (offlineMinutes < 1 || offlineMinutes > MaxMinutes)
            {
                errors.Add($"A camera is offline for 1 to {MaxMinutes} minutes before it alerts.");
            }

            if (cooldown < 0 || cooldown > MaxMinutes)
            {
                errors.Add($"The quiet time after an alert is 0 to {MaxMinutes} minutes.");
            }

            var sites = Distinct(request.SiteIds);
            var known = (await _tenancy.GetSitesAsync(rule.OrganizationId, cancellationToken).ConfigureAwait(false)).Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (sites.Any(s => !known.Contains(s)))
            {
                errors.Add("Some of the sites do not exist.");
            }

            var cameras = Distinct(request.CameraIds);
            if (cameras.Any(c => _directory.Find(c)?.OrganizationId != rule.OrganizationId))
            {
                errors.Add("Some of the cameras do not exist.");
            }

            var hours = request.Hours ?? new List<ScheduleWindow>();
            if (hours.Count > 0)
            {
                errors.AddRange(RecordingSchedule.Check(hours));
            }

            var emails = Distinct(request.Emails);
            if (emails.Count > MaxEmails)
            {
                errors.Add($"A rule emails at most {MaxEmails} addresses.");
            }

            foreach (var email in emails.Where(e => !IsEmail(e)))
            {
                errors.Add($"'{email}' is not an email address.");
            }

            var webhook = string.IsNullOrWhiteSpace(request.WebhookUrl) ? null : request.WebhookUrl.Trim();
            if (webhook != null && (webhook.Length > 1000 || !Uri.TryCreate(webhook, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
            {
                errors.Add("The webhook is an http or https address.");
            }

            if (request.WebhookSecret?.Length > 200)
            {
                errors.Add("The webhook secret has at most 200 characters.");
            }

            if (errors.Count > 0)
            {
                return ServiceResult<AlertRule>.Invalid(errors);
            }

            rule.Name = name;
            rule.Enabled = request.Enabled ?? rule.Enabled;
            rule.Kind = kind;
            rule.Categories = kind == AlertKinds.Event ? categories : new List<string>();
            rule.MinSeverity = kind == AlertKinds.Event ? severity : null;
            rule.OfflineMinutes = offlineMinutes;
            rule.SiteIds = sites;
            rule.CameraIds = cameras;
            rule.Hours = hours.Count == 0 ? new List<ScheduleWindow>() : RecordingSchedule.Deserialize(RecordingSchedule.Serialize(hours));
            rule.CooldownMinutes = cooldown;
            rule.Emails = emails;
            rule.WebhookUrl = webhook;
            rule.WebhookSecret = request.WebhookSecret == null ? rule.WebhookSecret : (request.WebhookSecret.Length == 0 ? null : request.WebhookSecret);
            rule.UpdatedAt = now;

            await _store.SaveRuleAsync(rule, cancellationToken).ConfigureAwait(false);
            _rules.Invalidate();
            return ServiceResult<AlertRule>.Ok(rule);
        }

        private static List<string> Distinct(IEnumerable<string> values) =>
            (values ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static bool IsEmail(string text) =>
            text.Length <= 254 && MailAddress.TryCreate(text, out var address) && address.Address == text && address.Host.Contains('.');

        #endregion
    }
}
