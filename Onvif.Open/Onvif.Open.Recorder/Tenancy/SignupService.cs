// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Mail;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Tenancy
{
    public sealed class SignupRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string OrganizationName { get; set; }
        public string SiteName { get; set; }
        public string TimeZone { get; set; }
    }

    /// <summary>
    /// Self-service sign-up in the cloud: a customer gives their email, a password and their organization; a link is
    /// emailed to them, and opening it creates the organization (in this deployment's region), its first site, the
    /// default policies and its owner (who signs in with the email). Until then nothing exists but the pending sign-up:
    /// a hash of the link's token and the password encrypted with a key that expires with it. The answer is the same
    /// whether or not the email has an account already (that email says so instead), so sign-up tells no one who is a
    /// customer.
    /// </summary>
    public sealed class SignupService
    {
        public SignupService(IDbContextFactory<RecorderDbContext> contexts, TenancyService tenancy, UserService users, IMailSender mail, CloudOptions cloud, IDataProtectionProvider dataProtection, ILogger<SignupService> logger, TimeProvider timeProvider = null)
        {
            _contexts = contexts;
            _tenancy = tenancy;
            _users = users;
            _mail = mail;
            _cloud = cloud ?? new CloudOptions();
            _passwords = dataProtection.CreateProtector("Onvif.Open.Recorder.SignupPasswords").ToTimeLimitedDataProtector();
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        public const int MaxEmailLength = 64;
        public const int MaxPendingPerEmail = 3;

        private readonly IDbContextFactory<RecorderDbContext> _contexts;
        private readonly TenancyService _tenancy;
        private readonly UserService _users;
        private readonly IMailSender _mail;
        private readonly CloudOptions _cloud;
        private readonly ITimeLimitedDataProtector _passwords;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;

        /// <summary>Whether customers can sign up here: the cloud is on and email can be sent.</summary>
        public bool IsAvailable => _cloud.Enabled && _mail.IsConfigured;

        /// <summary>Checks the request and emails the link (or, for an email with an account, a note saying so).</summary>
        public async Task<ServiceResult<bool>> StartAsync(SignupRequest request, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable)
            {
                return ServiceResult<bool>.NotFound();
            }

            var email = request?.Email?.Trim() ?? string.Empty;
            var errors = new List<string>();
            if (email.Length > MaxEmailLength || !MailAddress.TryCreate(email, out var address) || address.Address != email || !address.Host.Contains('.'))
            {
                errors.Add($"Enter your email address (at most {MaxEmailLength} characters).");
            }

            errors.AddRange(UserService.CheckPasswordRules(request?.Password));
            if (string.IsNullOrWhiteSpace(request?.OrganizationName) || request.OrganizationName.Trim().Length > 100)
            {
                errors.Add("Name your organization (at most 100 characters).");
            }

            if (string.IsNullOrWhiteSpace(request?.SiteName) || request.SiteName.Trim().Length > 100)
            {
                errors.Add("Name your first site (at most 100 characters).");
            }

            if (string.IsNullOrWhiteSpace(request?.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone.Trim(), out _))
            {
                errors.Add("Choose your site's time zone.");
            }

            if (errors.Count > 0)
            {
                return ServiceResult<bool>.Invalid(errors);
            }

            if (await _users.FindByNameAsync(email, cancellationToken).ConfigureAwait(false) != null)
            {
                await SendAsync(email, "Your Onvif.Open account", $"Someone (perhaps you) tried to sign up with this email, which already has an account.\n\nSign in at {_cloud.WebUrl}/login, or reset your password with your organization's owner.\n\nIf it was not you, you can ignore this email.", cancellationToken).ConfigureAwait(false);
                return ServiceResult<bool>.Ok(true);
            }

            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                // A few attempts per email at a time (a lost email, a typo): older ones go.
                var pending = await db.Signups.Where(s => s.Email == email).OrderByDescending(s => s.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);
                db.Signups.RemoveRange(pending.Skip(MaxPendingPerEmail - 1));
                db.Signups.Add(new SignupRecord
                {
                    Id = Guid.NewGuid().ToString("D"),
                    TokenHash = Hash(token),
                    Email = email,
                    Password = _passwords.Protect(request.Password, _cloud.SignupLifetime),
                    OrganizationName = request.OrganizationName.Trim(),
                    SiteName = request.SiteName.Trim(),
                    TimeZone = request.TimeZone.Trim(),
                    CreatedAt = now,
                    ExpiresAt = now + _cloud.SignupLifetime
                });
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }

            await SendAsync(email, "Confirm your Onvif.Open sign-up", $"Welcome to Onvif.Open.\n\nOpen this link to create {request.OrganizationName.Trim()} and sign in:\n{_cloud.WebUrl}/signup/verify?token={token}\n\nThe link works for {_cloud.SignupLifetime.TotalHours:0} hours. If you did not sign up, you can ignore this email.", cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation("Sign-up started for {Organization}", request.OrganizationName.Trim());
            return ServiceResult<bool>.Ok(true);
        }

        /// <summary>The link was opened: the organization and its owner are created, once; the owner is returned to sign in.</summary>
        public async Task<ServiceResult<Member>> VerifyAsync(string token, CancellationToken cancellationToken = default)
        {
            if (!_cloud.Enabled || string.IsNullOrWhiteSpace(token) || token.Length > 100)
            {
                return ServiceResult<Member>.NotFound();
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            SignupRecord signup;
            await using (var db = await _contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                await db.Signups.Where(s => s.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
                var hash = Hash(token);
                signup = await db.Signups.FirstOrDefaultAsync(s => s.TokenHash == hash, cancellationToken).ConfigureAwait(false);
                if (signup == null)
                {
                    return ServiceResult<Member>.NotFound();
                }

                // One use: whatever happens next, this link is spent.
                await db.Signups.Where(s => s.Email == signup.Email).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            }

            string password;
            try
            {
                password = _passwords.Unprotect(signup.Password);
            }
            catch (CryptographicException)
            {
                return ServiceResult<Member>.NotFound();
            }

            var created = await _tenancy.CreateOrganizationAsync(new SetupRequest
            {
                OrganizationName = signup.OrganizationName,
                Region = _cloud.Region,
                SiteName = signup.SiteName,
                TimeZone = signup.TimeZone,
                UserName = signup.Email,
                Password = password
            }, cancellationToken).ConfigureAwait(false);

            if (created.Status == ServiceStatus.Ok)
            {
                _logger?.LogInformation("Organization {Organization} signed up ({Region})", signup.OrganizationName, _cloud.Region);
            }

            return created;
        }

        private async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
        {
            using (var message = new MailMessage { From = new MailAddress(_mail.From), Subject = subject, Body = body })
            {
                message.To.Add(to);
                await _mail.SendAsync(message, cancellationToken).ConfigureAwait(false);
            }
        }

        private static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
