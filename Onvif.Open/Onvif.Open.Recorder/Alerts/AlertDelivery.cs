// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Alerts
{
    /// <summary>How one notification went.</summary>
    public sealed record DeliveryResult(bool Ok, string Error);

    /// <summary>
    /// The enabled rules in memory, loaded on first use and again after each change.
    /// </summary>
    public sealed class AlertRuleCache
    {
        public AlertRuleCache(IAlertStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        private readonly IAlertStore _store;
        private volatile Task<IReadOnlyList<AlertRule>> _rules;

        public async Task<IReadOnlyList<AlertRule>> GetAsync(CancellationToken cancellationToken = default)
        {
            var rules = _rules ??= LoadAsync();
            try
            {
                return await rules.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Try again next time.
                if (_rules == rules)
                {
                    _rules = null;
                }

                throw;
            }
        }

        public void Invalidate() => _rules = null;

        private async Task<IReadOnlyList<AlertRule>> LoadAsync() =>
            (await _store.GetRulesAsync(null).ConfigureAwait(false)).Where(r => r.Enabled).ToList();
    }

    /// <summary>
    /// Tells people about an alert in every way its rule asks for, and notes how each went in the alert's delivery.
    /// A failed notification does not stop the others.
    /// </summary>
    public sealed class AlertDelivery
    {
        /// <param name="appUrl">The web app's address ("Alerts:PublicUrl"), for links in notifications; null: no links.</param>
        public AlertDelivery(IEnumerable<IAlertNotifier> notifiers, ITenancyStore tenancy, string appUrl, ILogger<AlertDelivery> logger)
        {
            _notifiers = (notifiers ?? Array.Empty<IAlertNotifier>()).ToList();
            _tenancy = tenancy ?? throw new ArgumentNullException(nameof(tenancy));
            _link = string.IsNullOrWhiteSpace(appUrl) ? null : appUrl.TrimEnd('/') + "/alerts";
            _logger = logger;
        }

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly IReadOnlyList<IAlertNotifier> _notifiers;
        private readonly ITenancyStore _tenancy;
        private readonly string _link;
        private readonly ILogger _logger;

        public static IReadOnlyDictionary<string, DeliveryResult> Read(string delivery) =>
            string.IsNullOrEmpty(delivery) ? new Dictionary<string, DeliveryResult>() : JsonSerializer.Deserialize<Dictionary<string, DeliveryResult>>(delivery, Json);

        /// <summary>Sends the notifications and stores how they went in <see cref="AlertRecord.Delivery"/>.</summary>
        public async Task<IReadOnlyDictionary<string, DeliveryResult>> DeliverAsync(AlertRule rule, AlertRecord alert, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, DeliveryResult>(StringComparer.Ordinal);
            var used = _notifiers.Where(n => n.AppliesTo(rule)).ToList();
            if (used.Count > 0)
            {
                var site = alert.SiteId == null ? null : (await _tenancy.FindSiteAsync(alert.SiteId, cancellationToken).ConfigureAwait(false))?.Name;
                var notice = new AlertNotice(rule, alert, site, _link);
                foreach (var notifier in used)
                {
                    try
                    {
                        await notifier.SendAsync(notice, cancellationToken).ConfigureAwait(false);
                        results[notifier.Name] = new DeliveryResult(true, null);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning("Alert rule {Rule}: the {Notifier} notification failed: {Error}", rule.Name, notifier.Name, ex.Message);
                        results[notifier.Name] = new DeliveryResult(false, ex is OperationCanceledException ? "Timed out." : ex.Message);
                    }
                }
            }

            alert.Delivery = results.Count == 0 ? null : JsonSerializer.Serialize(results, Json);
            return results;
        }
    }
}
