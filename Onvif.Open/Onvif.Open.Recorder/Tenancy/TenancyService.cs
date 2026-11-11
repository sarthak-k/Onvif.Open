// Sarthak Killedar
// 2026-11-09

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Recorder.Tenancy
{
    /// <summary>What the first-run setup asks for.</summary>
    public sealed class SetupRequest
    {
        public string OrganizationName { get; set; }
        public string Region { get; set; }
        public string SiteName { get; set; }
        public string TimeZone { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }

        /// <summary>Required only when Security:SetupToken is configured.</summary>
        public string SetupToken { get; set; }
    }

    public sealed class SiteRequest
    {
        public string Name { get; set; }
        public string TimeZone { get; set; }
        public string Address { get; set; }
    }

    public sealed class PolicyRequest
    {
        public string Name { get; set; }
        public string Mode { get; set; }
        public int? PreRollSeconds { get; set; }
        public int? PostRollSeconds { get; set; }
        public int? ContinuousRetentionDays { get; set; }
        public int? EventRetentionDays { get; set; }
        public bool? IsDefault { get; set; }

        /// <summary>The weekly hours of a "Schedule" policy; null keeps the stored ones.</summary>
        public List<ScheduleWindow> Schedule { get; set; }
    }

    /// <summary>
    /// The customer's structure: first-run setup (organization, first site, default recording policies, owner),
    /// sites, recording policies and the organization itself. A changed policy is applied to its cameras at once.
    /// </summary>
    public sealed class TenancyService
    {
        #region Constructor

        public TenancyService(ITenancyStore store, UserService users, CameraConfigurationService cameras, SecurityOptions security, ILogger<TenancyService> logger, TimeProvider timeProvider = null, CloudOptions cloud = null, Billing.BillingOptions billing = null)
        {
            _cloud = cloud ?? new CloudOptions();
            _billing = billing ?? new Billing.BillingOptions();
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _users = users ?? throw new ArgumentNullException(nameof(users));
            _cameras = cameras ?? throw new ArgumentNullException(nameof(cameras));
            _security = security ?? new SecurityOptions();
            _logger = logger;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        #endregion

        #region Private Variables

        public const int MaxNameLength = 100;
        public const int MaxRetentionDays = 3650;

        /// <summary>Defaults agreed for every new organization: continuous video 7 days, video around events 30 days.</summary>
        public const int DefaultContinuousRetentionDays = 7;
        public const int DefaultEventRetentionDays = 30;

        private readonly CloudOptions _cloud;
        private readonly Billing.BillingOptions _billing;
        private readonly ITenancyStore _store;
        private readonly UserService _users;
        private readonly CameraConfigurationService _cameras;
        private readonly SecurityOptions _security;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private readonly SemaphoreSlim _setup = new SemaphoreSlim(1, 1);

        #endregion

        #region Public Methods - Setup

        /// <summary>True until the first owner exists.</summary>
        /// <summary>Whether the recorder still needs its first owner; never in the cloud (customers sign up there).</summary>
        public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default) => !_cloud.Enabled && !await _users.AnyAsync(cancellationToken).ConfigureAwait(false);

        /// <summary>Creates the organization, its first site, the default recording policies and the owner; once only.</summary>
        public async Task<ServiceResult<Member>> CompleteSetupAsync(SetupRequest request, CancellationToken cancellationToken = default)
        {
            await _setup.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (!await IsSetupRequiredAsync(cancellationToken).ConfigureAwait(false))
                {
                    return ServiceResult<Member>.Conflict("Setup is already done: sign in instead.");
                }

                if (!string.IsNullOrEmpty(_security.SetupToken) && !TokensMatch(_security.SetupToken, request?.SetupToken))
                {
                    return ServiceResult<Member>.Forbidden("The setup token is wrong.");
                }

                var created = await CreateOrganizationAsync(request, cancellationToken).ConfigureAwait(false);
                if (created.Status == ServiceStatus.Ok)
                {
                    _logger.LogInformation("Setup done: organization {Organization} with owner {UserName}", request.OrganizationName, created.Value.User.UserName);
                }

                return created;
            }
            finally
            {
                _setup.Release();
            }
        }

        /// <summary>
        /// A new organization with its first site, the default recording policies and its owner (setup, and sign-up in
        /// the cloud). The owner's password is checked like any other.
        /// </summary>
        public async Task<ServiceResult<Member>> CreateOrganizationAsync(SetupRequest request, CancellationToken cancellationToken = default)
        {
            var errors = new List<string>();
            var region = Regions.Normalize(request?.Region);
            CheckName(request?.OrganizationName, "Organization name", errors);
            if (region == null)
            {
                errors.Add($"Region must be one of {string.Join(", ", Regions.All.Select(r => r.Id))}.");
            }

            CheckSite(new SiteRequest { Name = request?.SiteName, TimeZone = request?.TimeZone }, errors);
            errors.AddRange(UserService.CheckPasswordRules(request?.Password));

            if (errors.Count > 0)
            {
                return ServiceResult<Member>.Invalid(errors);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var organization = new OrganizationRecord { Id = NewId(), Name = request.OrganizationName.Trim(), Region = region, CreatedAt = now };
            _billing.StartTrial(organization, now);
            await _store.SaveOrganizationAsync(organization, cancellationToken).ConfigureAwait(false);
            await _store.SaveSiteAsync(new SiteRecord { Id = NewId(), OrganizationId = organization.Id, Name = request.SiteName.Trim(), TimeZone = request.TimeZone.Trim(), CreatedAt = now, UpdatedAt = now }, cancellationToken).ConfigureAwait(false);

            foreach (var policy in DefaultPolicies(organization.Id, now))
            {
                await _store.SavePolicyAsync(policy, cancellationToken).ConfigureAwait(false);
            }

            var owner = await _users.CreateMemberAsync(organization.Id, Roles.Owner, request.UserName, request.Password, Roles.Owner, null, mustChangePassword: false, cancellationToken).ConfigureAwait(false);
            if (owner.Status != UserStatus.Ok)
            {
                return ServiceResult<Member>.Invalid(owner.Errors);
            }

            return ServiceResult<Member>.Ok(owner.Member);
        }

        /// <summary>
        /// Automated installs: with Security:InitialAdmin:Password configured and no users yet, setup is done at startup
        /// with a default organization and site. Otherwise the web application's first-run setup does it.
        /// </summary>
        public async Task EnsureConfiguredOwnerAsync(CancellationToken cancellationToken = default)
        {
            var initial = _security.InitialAdmin;
            if (string.IsNullOrEmpty(initial?.Password) || !await IsSetupRequiredAsync(cancellationToken).ConfigureAwait(false))
            {
                if (await IsSetupRequiredAsync(cancellationToken).ConfigureAwait(false))
                {
                    _logger.LogWarning("No users yet: open the web application to set up the recorder");
                }

                return;
            }

            var result = await CompleteSetupAsync(new SetupRequest
            {
                OrganizationName = initial.OrganizationName ?? "My organization",
                Region = initial.Region ?? Regions.UnitedStates,
                SiteName = initial.SiteName ?? "Main site",
                TimeZone = initial.TimeZone ?? "UTC",
                UserName = initial.UserName ?? "admin",
                Password = initial.Password,
                SetupToken = _security.SetupToken
            }, cancellationToken).ConfigureAwait(false);

            if (result.Status != ServiceStatus.Ok)
            {
                throw new InvalidOperationException("Could not create the configured owner: " + string.Join(" ", result.Errors));
            }
        }

        #endregion

        #region Public Methods - Organization

        public Task<OrganizationRecord> GetOrganizationAsync(string organizationId, CancellationToken cancellationToken = default) => _store.FindOrganizationAsync(organizationId, cancellationToken);

        /// <summary>The name changes; the data region does not.</summary>
        /// <summary>
        /// Renames the organization and/or sets whether every member needs a second factor (null: unchanged). Requiring
        /// it needs it on for the owner making the change, who would otherwise be stopped at their next sign-in.
        /// </summary>
        public async Task<ServiceResult<OrganizationRecord>> UpdateOrganizationAsync(string organizationId, string actorUserId, string name, bool? requireTwoFactor, CancellationToken cancellationToken = default)
        {
            var organization = await _store.FindOrganizationAsync(organizationId, cancellationToken).ConfigureAwait(false);
            if (organization == null)
            {
                return ServiceResult<OrganizationRecord>.NotFound();
            }

            var errors = new List<string>();
            if (name != null || requireTwoFactor == null)
            {
                CheckName(name, "Organization name", errors);
            }

            if (requireTwoFactor == true && !organization.RequireTwoFactor
                && Security.TwoFactorStates.Of(await _users.FindAsync(actorUserId, cancellationToken).ConfigureAwait(false)) != Security.TwoFactorStates.On)
            {
                errors.Add("Turn on two-factor sign-in for your own account first (Account), then require it for everyone.");
            }

            if (errors.Count > 0)
            {
                return ServiceResult<OrganizationRecord>.Invalid(errors);
            }

            organization.Name = name?.Trim() ?? organization.Name;
            organization.RequireTwoFactor = requireTwoFactor ?? organization.RequireTwoFactor;
            await _store.SaveOrganizationAsync(organization, cancellationToken).ConfigureAwait(false);
            return ServiceResult<OrganizationRecord>.Ok(organization);
        }

        #endregion

        #region Public Methods - Sites

        /// <summary>The organization's sites, only those in <paramref name="visible"/> when given.</summary>
        public async Task<IReadOnlyList<SiteRecord>> GetSitesAsync(string organizationId, IReadOnlySet<string> visible, CancellationToken cancellationToken = default)
        {
            var sites = await _store.GetSitesAsync(organizationId, cancellationToken).ConfigureAwait(false);
            return visible == null ? sites : sites.Where(s => visible.Contains(s.Id)).ToList();
        }

        public async Task<ServiceResult<SiteRecord>> CreateSiteAsync(string organizationId, SiteRequest request, CancellationToken cancellationToken = default)
        {
            var errors = new List<string>();
            CheckSite(request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<SiteRecord>.Invalid(errors);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var site = new SiteRecord { Id = NewId(), OrganizationId = organizationId, CreatedAt = now };
            Apply(site, request, now);
            await _store.SaveSiteAsync(site, cancellationToken).ConfigureAwait(false);
            return ServiceResult<SiteRecord>.Ok(site);
        }

        public async Task<ServiceResult<SiteRecord>> UpdateSiteAsync(string organizationId, string siteId, SiteRequest request, CancellationToken cancellationToken = default)
        {
            var site = await FindAsync(_store.FindSiteAsync(siteId, cancellationToken), s => s.OrganizationId, organizationId).ConfigureAwait(false);
            if (site == null)
            {
                return ServiceResult<SiteRecord>.NotFound();
            }

            var errors = new List<string>();
            CheckSite(request, errors);
            if (errors.Count > 0)
            {
                return ServiceResult<SiteRecord>.Invalid(errors);
            }

            Apply(site, request, _timeProvider.GetUtcNow().UtcDateTime);
            await _store.SaveSiteAsync(site, cancellationToken).ConfigureAwait(false);
            return ServiceResult<SiteRecord>.Ok(site);
        }

        /// <summary>Refused while the site has cameras, and for an organization's last site.</summary>
        public async Task<ServiceResult<SiteRecord>> DeleteSiteAsync(string organizationId, string siteId, CancellationToken cancellationToken = default)
        {
            var site = await FindAsync(_store.FindSiteAsync(siteId, cancellationToken), s => s.OrganizationId, organizationId).ConfigureAwait(false);
            if (site == null)
            {
                return ServiceResult<SiteRecord>.NotFound();
            }

            var cameras = await _store.CountCamerasAsync(siteId: siteId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (cameras > 0)
            {
                return ServiceResult<SiteRecord>.Conflict($"Site '{site.Name}' has {cameras} camera(s): move or remove them first.");
            }

            if ((await _store.GetSitesAsync(organizationId, cancellationToken).ConfigureAwait(false)).Count == 1)
            {
                return ServiceResult<SiteRecord>.Conflict("An organization needs at least one site.");
            }

            await _store.DeleteSiteAsync(siteId, cancellationToken).ConfigureAwait(false);
            return ServiceResult<SiteRecord>.Ok(site);
        }

        #endregion

        #region Public Methods - Recording policies

        public Task<IReadOnlyList<RecordingPolicyRecord>> GetPoliciesAsync(string organizationId, CancellationToken cancellationToken = default) => _store.GetPoliciesAsync(organizationId, cancellationToken);

        public async Task<ServiceResult<RecordingPolicyRecord>> CreatePolicyAsync(string organizationId, PolicyRequest request, CancellationToken cancellationToken = default)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var policy = new RecordingPolicyRecord
            {
                Id = NewId(),
                OrganizationId = organizationId,
                Mode = RecordingModes.Always,
                PreRollSeconds = 5,
                PostRollSeconds = 10,
                ContinuousRetentionDays = DefaultContinuousRetentionDays,
                EventRetentionDays = DefaultEventRetentionDays,
                CreatedAt = now
            };

            var errors = Apply(policy, request, now);
            if (errors.Count > 0)
            {
                return ServiceResult<RecordingPolicyRecord>.Invalid(errors);
            }

            await SaveWithDefaultAsync(policy, cancellationToken).ConfigureAwait(false);
            return ServiceResult<RecordingPolicyRecord>.Ok(policy);
        }

        /// <summary>Changes a policy and applies it to its cameras at once (they reconnect with the new mode).</summary>
        public async Task<ServiceResult<RecordingPolicyRecord>> UpdatePolicyAsync(string organizationId, string policyId, PolicyRequest request, CancellationToken cancellationToken = default)
        {
            var policy = await FindAsync(_store.FindPolicyAsync(policyId, cancellationToken), p => p.OrganizationId, organizationId).ConfigureAwait(false);
            if (policy == null)
            {
                return ServiceResult<RecordingPolicyRecord>.NotFound();
            }

            if (request?.IsDefault == false && policy.IsDefault)
            {
                return ServiceResult<RecordingPolicyRecord>.Invalid(new[] { "Make another policy the default instead." });
            }

            var errors = Apply(policy, request, _timeProvider.GetUtcNow().UtcDateTime);
            if (errors.Count > 0)
            {
                return ServiceResult<RecordingPolicyRecord>.Invalid(errors);
            }

            await SaveWithDefaultAsync(policy, cancellationToken).ConfigureAwait(false);
            await _cameras.ApplyPolicyAsync(policy, cancellationToken).ConfigureAwait(false);
            return ServiceResult<RecordingPolicyRecord>.Ok(policy);
        }

        /// <summary>Refused for the default policy and while cameras use it.</summary>
        public async Task<ServiceResult<RecordingPolicyRecord>> DeletePolicyAsync(string organizationId, string policyId, CancellationToken cancellationToken = default)
        {
            var policy = await FindAsync(_store.FindPolicyAsync(policyId, cancellationToken), p => p.OrganizationId, organizationId).ConfigureAwait(false);
            if (policy == null)
            {
                return ServiceResult<RecordingPolicyRecord>.NotFound();
            }

            if (policy.IsDefault)
            {
                return ServiceResult<RecordingPolicyRecord>.Conflict("The default policy cannot be deleted: make another policy the default first.");
            }

            var cameras = await _store.CountCamerasAsync(policyId: policyId, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (cameras > 0)
            {
                return ServiceResult<RecordingPolicyRecord>.Conflict($"{cameras} camera(s) use '{policy.Name}': give them another policy first.");
            }

            await _store.DeletePolicyAsync(policyId, cancellationToken).ConfigureAwait(false);
            return ServiceResult<RecordingPolicyRecord>.Ok(policy);
        }

        #endregion

        #region Private Methods

        private static IEnumerable<RecordingPolicyRecord> DefaultPolicies(string organizationId, DateTime now)
        {
            yield return new RecordingPolicyRecord
            {
                Id = NewId(), OrganizationId = organizationId, Name = "Always record", Mode = RecordingModes.Always, PreRollSeconds = 5, PostRollSeconds = 10,
                ContinuousRetentionDays = DefaultContinuousRetentionDays, EventRetentionDays = DefaultEventRetentionDays, IsDefault = true, CreatedAt = now, UpdatedAt = now
            };
            yield return new RecordingPolicyRecord
            {
                Id = NewId(), OrganizationId = organizationId, Name = "Events only", Mode = RecordingModes.EventsOnly, PreRollSeconds = 5, PostRollSeconds = 10,
                ContinuousRetentionDays = DefaultContinuousRetentionDays, EventRetentionDays = DefaultEventRetentionDays, CreatedAt = now, UpdatedAt = now
            };
            yield return new RecordingPolicyRecord
            {
                Id = NewId(), OrganizationId = organizationId, Name = "Business hours", Mode = RecordingModes.Schedule, PreRollSeconds = 5, PostRollSeconds = 10,
                ContinuousRetentionDays = DefaultContinuousRetentionDays, EventRetentionDays = DefaultEventRetentionDays,
                Schedule = RecordingSchedule.Serialize(RecordingSchedule.BusinessHours), CreatedAt = now, UpdatedAt = now
            };
        }

        /// <summary>One default per organization: making this one the default unmarks the previous one.</summary>
        private async Task SaveWithDefaultAsync(RecordingPolicyRecord policy, CancellationToken cancellationToken)
        {
            if (policy.IsDefault)
            {
                foreach (var other in (await _store.GetPoliciesAsync(policy.OrganizationId, cancellationToken).ConfigureAwait(false)).Where(p => p.IsDefault && p.Id != policy.Id))
                {
                    other.IsDefault = false;
                    await _store.SavePolicyAsync(other, cancellationToken).ConfigureAwait(false);
                }
            }

            await _store.SavePolicyAsync(policy, cancellationToken).ConfigureAwait(false);
        }

        private static List<string> Apply(RecordingPolicyRecord policy, PolicyRequest request, DateTime now)
        {
            var errors = new List<string>();
            if (request == null)
            {
                errors.Add("A policy is required.");
                return errors;
            }

            var name = request.Name ?? policy.Name;
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 64)
            {
                errors.Add("Name must be 1 to 64 characters.");
            }

            var mode = request.Mode == null ? policy.Mode : RecordingModes.Normalize(request.Mode);
            if (mode == null)
            {
                errors.Add($"Mode must be one of {string.Join(", ", RecordingModes.All)}.");
            }

            Range(request.PreRollSeconds, 0, 120, "Pre-roll seconds", errors);
            Range(request.PostRollSeconds, 0, 600, "Post-roll seconds", errors);
            Range(request.ContinuousRetentionDays, 1, MaxRetentionDays, "Continuous retention days", errors);
            Range(request.EventRetentionDays, 1, MaxRetentionDays, "Event retention days", errors);

            var schedule = request.Schedule ?? RecordingSchedule.Deserialize(policy.Schedule);
            if (mode == RecordingModes.Schedule)
            {
                errors.AddRange(RecordingSchedule.Check(schedule));
            }
            else if (request.Schedule?.Count > 0)
            {
                errors.AddRange(RecordingSchedule.Check(schedule));
            }

            if (errors.Count > 0)
            {
                return errors;
            }

            policy.Name = name.Trim();
            policy.Mode = mode;
            policy.PreRollSeconds = request.PreRollSeconds ?? policy.PreRollSeconds;
            policy.PostRollSeconds = request.PostRollSeconds ?? policy.PostRollSeconds;
            policy.ContinuousRetentionDays = request.ContinuousRetentionDays ?? policy.ContinuousRetentionDays;
            policy.EventRetentionDays = request.EventRetentionDays ?? policy.EventRetentionDays;
            policy.IsDefault = request.IsDefault ?? policy.IsDefault;
            policy.Schedule = RecordingSchedule.Serialize(schedule);
            policy.UpdatedAt = now;

            if (policy.EventRetentionDays < policy.ContinuousRetentionDays && policy.Mode != RecordingModes.EventsOnly)
            {
                errors.Add("Video around events is kept at least as long as continuous video.");
            }

            return errors;
        }

        private static void Range(int? value, int min, int max, string what, List<string> errors)
        {
            if (value.HasValue && (value < min || value > max))
            {
                errors.Add($"{what} must be {min} to {max}.");
            }
        }

        private static void Apply(SiteRecord site, SiteRequest request, DateTime now)
        {
            site.Name = request.Name.Trim();
            site.TimeZone = request.TimeZone.Trim();
            site.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
            site.UpdatedAt = now;
        }

        private static void CheckSite(SiteRequest request, List<string> errors)
        {
            CheckName(request?.Name, "Site name", errors);

            if (string.IsNullOrWhiteSpace(request?.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(request.TimeZone.Trim(), out _))
            {
                errors.Add("Time zone must be an IANA time zone such as \"Asia/Kolkata\" or \"America/New_York\".");
            }

            if (request?.Address?.Length > 200)
            {
                errors.Add("Address must be at most 200 characters.");
            }
        }

        private static void CheckName(string name, string what, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > MaxNameLength)
            {
                errors.Add($"{what} must be 1 to {MaxNameLength} characters.");
            }
        }

        /// <summary>A record of another organization is reported as not found.</summary>
        private static async Task<T> FindAsync<T>(Task<T> find, Func<T, string> organizationOf, string organizationId)
            where T : class
        {
            var record = await find.ConfigureAwait(false);
            return record != null && string.Equals(organizationOf(record), organizationId, StringComparison.Ordinal) ? record : null;
        }

        private static bool TokensMatch(string expected, string actual)
        {
            return actual != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
        }

        private static string NewId() => Guid.NewGuid().ToString("D");

        #endregion
    }
}
