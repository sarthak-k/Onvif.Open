// Sarthak Killedar
// 2026-11-10

using System;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Recorder.Alerts;

namespace Onvif.Open.Recorder.Mail
{
    /// <summary>Sends email (sign-up links); tests replace it.</summary>
    public interface IMailSender
    {
        bool IsConfigured { get; }

        /// <summary>The sender, e.g. "Onvif.Open &lt;no-reply@example.com&gt;".</summary>
        string From { get; }

        Task SendAsync(MailMessage message, CancellationToken cancellationToken = default);
    }

    /// <summary>Through the SMTP server of "Alerts:Smtp" (the same one alerts use).</summary>
    public sealed class SmtpMailSender : IMailSender
    {
        public SmtpMailSender(SmtpOptions options)
        {
            _options = options ?? new SmtpOptions();
        }

        private readonly SmtpOptions _options;

        public bool IsConfigured => _options.IsConfigured;

        public string From => _options.From;

        public async Task SendAsync(MailMessage message, CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("No mail server is set up (Alerts:Smtp).");
            }

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
}
