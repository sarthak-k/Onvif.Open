// Sarthak Killedar
// 2026-11-09

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Alerts
{
    /// <summary>An alert to tell someone about.</summary>
    /// <param name="SiteName">Null for alerts about no site.</param>
    /// <param name="Link">The alerts page of the web app, when "Alerts:PublicUrl" is set.</param>
    public sealed record AlertNotice(AlertRule Rule, AlertRecord Alert, string SiteName, string Link);

    /// <summary>
    /// One way of telling people about alerts (email, webhook, ...).
    /// </summary>
    public interface IAlertNotifier
    {
        /// <summary>The key of its result in an alert's delivery ("email", "webhook").</summary>
        string Name { get; }

        /// <summary>Whether the rule uses it.</summary>
        bool AppliesTo(AlertRule rule);

        /// <summary>Sends the notice; throws when it could not.</summary>
        Task SendAsync(AlertNotice notice, CancellationToken cancellationToken = default);
    }

    public static class AlertText
    {
        /// <summary>"Person at Front gate (Main office)".</summary>
        public static string Subject(AlertNotice notice)
        {
            var alert = notice.Alert;
            var where = alert.CameraName ?? notice.SiteName;
            var site = alert.CameraName != null && notice.SiteName != null ? $" ({notice.SiteName})" : string.Empty;
            return where == null ? alert.Title : $"{alert.Title} at {where}{site}";
        }
    }

    /// <summary>"Alerts:Smtp": the mail server alerts are sent through.</summary>
    public sealed class SmtpOptions
    {
        public const string SectionName = "Alerts:Smtp";

        public string Host { get; set; }
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string UserName { get; set; }
        public string Password { get; set; }

        /// <summary>The sender, e.g. "Onvif.Open &lt;alerts@example.com&gt;".</summary>
        public string From { get; set; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
    }

    /// <summary>
    /// Emails alerts through the SMTP server of "Alerts:Smtp".
    /// </summary>
    public sealed class EmailNotifier : IAlertNotifier
    {
        /// <param name="send">Sends a message (tests); by default through <see cref="SmtpClient"/>.</param>
        public EmailNotifier(SmtpOptions options, Func<MailMessage, CancellationToken, Task> send = null)
        {
            _options = options ?? new SmtpOptions();
            _send = send ?? SendWithSmtpAsync;
        }

        private readonly SmtpOptions _options;
        private readonly Func<MailMessage, CancellationToken, Task> _send;

        public string Name => "email";

        public bool AppliesTo(AlertRule rule) => rule.Emails.Count > 0;

        public async Task SendAsync(AlertNotice notice, CancellationToken cancellationToken = default)
        {
            if (!_options.IsConfigured)
            {
                throw new InvalidOperationException("No mail server is set up (Alerts:Smtp).");
            }

            using (var message = Compose(notice))
            {
                await _send(message, cancellationToken).ConfigureAwait(false);
            }
        }

        public MailMessage Compose(AlertNotice notice)
        {
            var alert = notice.Alert;
            var body = new StringBuilder()
                .AppendLine(AlertText.Subject(notice))
                .AppendLine()
                .AppendLine($"When: {DateTime.SpecifyKind(alert.TriggeredAt, DateTimeKind.Utc):yyyy-MM-dd HH:mm:ss} UTC")
                .AppendLine($"Severity: {alert.Severity}")
                .AppendLine($"Rule: {alert.RuleName}");
            if (notice.Link != null)
            {
                body.AppendLine().AppendLine($"Open the alerts: {notice.Link}");
            }

            var message = new MailMessage { From = new MailAddress(_options.From), Subject = $"[Onvif.Open] {AlertText.Subject(notice)}", Body = body.ToString() };
            foreach (var email in notice.Rule.Emails)
            {
                message.To.Add(email);
            }

            return message;
        }

        private async Task SendWithSmtpAsync(MailMessage message, CancellationToken cancellationToken)
        {
            using (var client = new SmtpClient(_options.Host, _options.Port) { EnableSsl = _options.EnableSsl, Timeout = 30_000 })
            {
                if (!string.IsNullOrEmpty(_options.UserName))
                {
                    client.Credentials = new System.Net.NetworkCredential(_options.UserName, _options.Password);
                }

                await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Posts alerts as JSON to the rule's webhook. With a secret, the body is signed: the header
    /// <c>X-OnvifOpen-Signature: sha256=&lt;hex HMAC-SHA256 of the body&gt;</c>.
    /// </summary>
    public sealed class WebhookNotifier : IAlertNotifier
    {
        public WebhookNotifier(HttpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public const string SignatureHeader = "X-OnvifOpen-Signature";
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly HttpClient _client;

        public string Name => "webhook";

        public bool AppliesTo(AlertRule rule) => !string.IsNullOrEmpty(rule.WebhookUrl);

        public static string Sign(string secret, byte[] body) =>
            "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

        public static byte[] Body(AlertNotice notice)
        {
            var alert = notice.Alert;
            return JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "alert",
                id = alert.Id,
                text = AlertText.Subject(notice),
                title = alert.Title,
                category = alert.Category,
                severity = alert.Severity,
                triggeredAt = DateTime.SpecifyKind(alert.TriggeredAt, DateTimeKind.Utc),
                rule = new { id = alert.RuleId, name = alert.RuleName },
                site = alert.SiteId == null ? null : new { id = alert.SiteId, name = notice.SiteName },
                camera = alert.CameraId == null ? null : new { id = alert.CameraId, name = alert.CameraName },
                eventId = alert.EventId,
                link = notice.Link
            }, Json);
        }

        public async Task SendAsync(AlertNotice notice, CancellationToken cancellationToken = default)
        {
            var body = Body(notice);
            using (var request = new HttpRequestMessage(HttpMethod.Post, notice.Rule.WebhookUrl) { Content = new ByteArrayContent(body) })
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                request.Headers.UserAgent.ParseAdd("Onvif.Open-Alerts/1.0");
                if (!string.IsNullOrEmpty(notice.Rule.WebhookSecret))
                {
                    request.Headers.Add(SignatureHeader, Sign(notice.Rule.WebhookSecret, body));
                }

                timeout.CancelAfter(Timeout);
                using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException($"The webhook answered {(int)response.StatusCode} {response.ReasonPhrase}.");
                    }
                }
            }
        }
    }
}
