// Sarthak Killedar
// 2026-11-08

using System;
using Microsoft.EntityFrameworkCore;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// The settings database (EF Core): works on SQLite, SQL Server, PostgreSQL and MySQL. Everything a customer owns
    /// belongs to an organization; cameras belong to a site. Ids are UUIDs and names are labels that can change.
    /// Camera connection options are one JSON column, so adding an option needs no schema change. Tables are prefixed
    /// "oo_" so they can live in a shared database.
    /// </summary>
    public sealed class RecorderDbContext : DbContext
    {
        #region Constructor

        public RecorderDbContext(DbContextOptions<RecorderDbContext> options)
            : base(options)
        {
        }

        #endregion

        #region Public Variables

        public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();
        public DbSet<SiteRecord> Sites => Set<SiteRecord>();
        public DbSet<RecordingPolicyRecord> RecordingPolicies => Set<RecordingPolicyRecord>();
        public DbSet<CameraRecord> Cameras => Set<CameraRecord>();
        public DbSet<UserRecord> Users => Set<UserRecord>();
        public DbSet<MembershipRecord> Memberships => Set<MembershipRecord>();
        public DbSet<LayoutRecord> Layouts => Set<LayoutRecord>();
        public DbSet<PluginRecord> Plugins => Set<PluginRecord>();
        public DbSet<EventRecord> Events => Set<EventRecord>();
        public DbSet<AuditRecord> Audit => Set<AuditRecord>();
        public DbSet<GatewayRecord> Gateways => Set<GatewayRecord>();
        public DbSet<CaseRecord> Cases => Set<CaseRecord>();
        public DbSet<CaseClipRecord> CaseClips => Set<CaseClipRecord>();
        public DbSet<ExportRecord> Exports => Set<ExportRecord>();
        public DbSet<ShareLinkRecord> ShareLinks => Set<ShareLinkRecord>();
        public DbSet<AlertRuleRecord> AlertRules => Set<AlertRuleRecord>();
        public DbSet<AlertRecord> Alerts => Set<AlertRecord>();
        public DbSet<SignupRecord> Signups => Set<SignupRecord>();

        /// <summary>Every table of the model, for creating the ones a database lacks.</summary>
        public static readonly string[] TableNames = { "oo_organizations", "oo_sites", "oo_recording_policies", "oo_cameras", "oo_users", "oo_memberships", "oo_layouts", "oo_plugins", "oo_events", "oo_audit", "oo_gateways", "oo_cases", "oo_case_clips", "oo_exports", "oo_share_links", "oo_alert_rules", "oo_alerts", "oo_signups" };

        #endregion

        #region Protected Methods

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<OrganizationRecord>(organization =>
            {
                organization.ToTable("oo_organizations");
                organization.HasKey(o => o.Id);
                organization.Property(o => o.Id).HasMaxLength(36);
                organization.Property(o => o.Name).HasMaxLength(100).IsRequired();
                organization.Property(o => o.Region).HasMaxLength(16).IsRequired();
            });

            modelBuilder.Entity<SiteRecord>(site =>
            {
                site.ToTable("oo_sites");
                site.HasKey(s => s.Id);
                site.Property(s => s.Id).HasMaxLength(36);
                site.Property(s => s.OrganizationId).HasMaxLength(36).IsRequired();
                site.Property(s => s.Name).HasMaxLength(100).IsRequired();
                site.Property(s => s.TimeZone).HasMaxLength(64).IsRequired();
                site.Property(s => s.Address).HasMaxLength(200);
                site.HasIndex(s => s.OrganizationId);
            });

            modelBuilder.Entity<RecordingPolicyRecord>(policy =>
            {
                policy.ToTable("oo_recording_policies");
                policy.HasKey(p => p.Id);
                policy.Property(p => p.Id).HasMaxLength(36);
                policy.Property(p => p.OrganizationId).HasMaxLength(36).IsRequired();
                policy.Property(p => p.Name).HasMaxLength(64).IsRequired();
                policy.Property(p => p.Mode).HasMaxLength(16).IsRequired();
                policy.HasIndex(p => p.OrganizationId);
            });

            modelBuilder.Entity<CameraRecord>(camera =>
            {
                camera.ToTable("oo_cameras");
                camera.HasKey(c => c.Id);
                camera.Property(c => c.Id).HasMaxLength(64);
                camera.Property(c => c.OrganizationId).HasMaxLength(36).IsRequired();
                camera.Property(c => c.SiteId).HasMaxLength(36).IsRequired();
                camera.Property(c => c.PolicyId).HasMaxLength(36).IsRequired();
                camera.Property(c => c.GatewayId).HasMaxLength(36).IsRequired();
                camera.Property(c => c.Name).HasMaxLength(100).IsRequired();
                camera.Property(c => c.Options).IsRequired();
                camera.HasIndex(c => c.OrganizationId);
                camera.HasIndex(c => c.SiteId);
                camera.HasIndex(c => c.CreatedAt);
            });

            modelBuilder.Entity<UserRecord>(user =>
            {
                user.ToTable("oo_users");
                user.HasKey(u => u.Id);
                user.Property(u => u.Id).HasMaxLength(36);
                user.Property(u => u.UserName).HasMaxLength(64).IsRequired();
                user.Property(u => u.NormalizedUserName).HasMaxLength(64).IsRequired();
                user.HasIndex(u => u.NormalizedUserName).IsUnique();
                user.Property(u => u.PasswordHash).IsRequired();
                user.Property(u => u.SecurityStamp).HasMaxLength(36).IsRequired();
            });

            modelBuilder.Entity<MembershipRecord>(membership =>
            {
                membership.ToTable("oo_memberships");
                membership.HasKey(m => m.Id);
                membership.Property(m => m.Id).HasMaxLength(36);
                membership.Property(m => m.UserId).HasMaxLength(36).IsRequired();
                membership.Property(m => m.OrganizationId).HasMaxLength(36).IsRequired();
                membership.Property(m => m.Role).HasMaxLength(16).IsRequired();
                membership.Property(m => m.Sites).IsRequired();
                membership.HasIndex(m => new { m.UserId, m.OrganizationId }).IsUnique();
                membership.HasIndex(m => m.OrganizationId);
            });

            modelBuilder.Entity<LayoutRecord>(layout =>
            {
                layout.ToTable("oo_layouts");
                layout.HasKey(l => l.Id);
                layout.Property(l => l.Id).HasMaxLength(36);
                layout.Property(l => l.OrganizationId).HasMaxLength(36).IsRequired();
                layout.Property(l => l.OwnerId).HasMaxLength(36);
                layout.Property(l => l.Name).HasMaxLength(64).IsRequired();
                layout.Property(l => l.Grid).HasMaxLength(32).IsRequired();
                layout.Property(l => l.Cameras).IsRequired();
                layout.HasIndex(l => new { l.OrganizationId, l.OwnerId });
            });

            modelBuilder.Entity<EventRecord>(evt =>
            {
                evt.ToTable("oo_events");
                evt.HasKey(e => e.Id);
                evt.Property(e => e.Id).HasMaxLength(36);
                evt.Property(e => e.OrganizationId).HasMaxLength(36).IsRequired();
                evt.Property(e => e.SiteId).HasMaxLength(36);
                evt.Property(e => e.CameraId).HasMaxLength(64);
                evt.Property(e => e.Category).HasMaxLength(32).IsRequired();
                evt.Property(e => e.Severity).HasMaxLength(16).IsRequired();
                evt.Property(e => e.Source).HasMaxLength(100).IsRequired();
                evt.Property(e => e.Title).HasMaxLength(200).IsRequired();
                evt.Property(e => e.Topic).HasMaxLength(256);
                evt.HasIndex(e => new { e.OrganizationId, e.StartTime });
                evt.HasIndex(e => new { e.CameraId, e.StartTime });
            });

            modelBuilder.Entity<AuditRecord>(audit =>
            {
                audit.ToTable("oo_audit");
                audit.HasKey(a => a.Id);
                audit.Property(a => a.Id).HasMaxLength(36);
                audit.Property(a => a.OrganizationId).HasMaxLength(36);
                audit.Property(a => a.UserId).HasMaxLength(36);
                audit.Property(a => a.UserName).HasMaxLength(64);
                audit.Property(a => a.Action).HasMaxLength(64).IsRequired();
                audit.Property(a => a.TargetType).HasMaxLength(32);
                audit.Property(a => a.TargetId).HasMaxLength(64);
                audit.Property(a => a.TargetName).HasMaxLength(200);
                audit.Property(a => a.IpAddress).HasMaxLength(64);
                audit.HasIndex(a => new { a.OrganizationId, a.CreatedAt });
            });

            modelBuilder.Entity<CaseRecord>(record =>
            {
                record.ToTable("oo_cases");
                record.HasKey(c => c.Id);
                record.Property(c => c.Id).HasMaxLength(36);
                record.Property(c => c.OrganizationId).HasMaxLength(36).IsRequired();
                record.Property(c => c.Name).HasMaxLength(200).IsRequired();
                record.Property(c => c.Status).HasMaxLength(16).IsRequired();
                record.Property(c => c.CreatedById).HasMaxLength(36);
                record.Property(c => c.CreatedByName).HasMaxLength(100);
                record.HasIndex(c => new { c.OrganizationId, c.UpdatedAt });
            });

            modelBuilder.Entity<CaseClipRecord>(clip =>
            {
                clip.ToTable("oo_case_clips");
                clip.HasKey(c => c.Id);
                clip.Property(c => c.Id).HasMaxLength(36);
                clip.Property(c => c.CaseId).HasMaxLength(36).IsRequired();
                clip.Property(c => c.OrganizationId).HasMaxLength(36).IsRequired();
                clip.Property(c => c.SiteId).HasMaxLength(36);
                clip.Property(c => c.Cameras).IsRequired();
                clip.Property(c => c.EventId).HasMaxLength(36);
                clip.Property(c => c.CreatedById).HasMaxLength(36);
                clip.Property(c => c.CreatedByName).HasMaxLength(100);
                clip.HasIndex(c => c.CaseId);
            });

            modelBuilder.Entity<ExportRecord>(export =>
            {
                export.ToTable("oo_exports");
                export.HasKey(e => e.Id);
                export.Property(e => e.Id).HasMaxLength(36);
                export.Property(e => e.OrganizationId).HasMaxLength(36).IsRequired();
                export.Property(e => e.SiteId).HasMaxLength(36);
                export.Property(e => e.CameraId).HasMaxLength(64).IsRequired();
                export.Property(e => e.CameraName).HasMaxLength(100);
                export.Property(e => e.CaseId).HasMaxLength(36);
                export.Property(e => e.RequestedById).HasMaxLength(36);
                export.Property(e => e.RequestedByName).HasMaxLength(100);
                export.Property(e => e.Status).HasMaxLength(16).IsRequired();
                export.Property(e => e.Sha256).HasMaxLength(64);
                export.HasIndex(e => new { e.OrganizationId, e.CreatedAt });
            });

            modelBuilder.Entity<ShareLinkRecord>(link =>
            {
                link.ToTable("oo_share_links");
                link.HasKey(l => l.Id);
                link.Property(l => l.Id).HasMaxLength(36);
                link.Property(l => l.ExportId).HasMaxLength(36).IsRequired();
                link.Property(l => l.OrganizationId).HasMaxLength(36).IsRequired();
                link.Property(l => l.TokenHash).HasMaxLength(64).IsRequired();
                link.Property(l => l.CreatedByName).HasMaxLength(100);
                link.HasIndex(l => l.TokenHash).IsUnique();
                link.HasIndex(l => l.ExportId);
            });

            modelBuilder.Entity<SignupRecord>(signup =>
            {
                signup.ToTable("oo_signups");
                signup.HasKey(s => s.Id);
                signup.Property(s => s.Id).HasMaxLength(36);
                signup.Property(s => s.TokenHash).HasMaxLength(64).IsRequired();
                signup.Property(s => s.Email).HasMaxLength(64).IsRequired();
                signup.Property(s => s.OrganizationName).HasMaxLength(100).IsRequired();
                signup.Property(s => s.SiteName).HasMaxLength(100).IsRequired();
                signup.Property(s => s.TimeZone).HasMaxLength(64).IsRequired();
                signup.HasIndex(s => s.TokenHash).IsUnique();
                signup.HasIndex(s => s.Email);
            });

            modelBuilder.Entity<AlertRuleRecord>(rule =>
            {
                rule.ToTable("oo_alert_rules");
                rule.HasKey(r => r.Id);
                rule.Property(r => r.Id).HasMaxLength(36);
                rule.Property(r => r.OrganizationId).HasMaxLength(36).IsRequired();
                rule.Property(r => r.Name).HasMaxLength(100).IsRequired();
                rule.Property(r => r.Kind).HasMaxLength(16).IsRequired();
                rule.Property(r => r.WebhookUrl).HasMaxLength(1000);
                rule.HasIndex(r => r.OrganizationId);
            });

            modelBuilder.Entity<AlertRecord>(alert =>
            {
                alert.ToTable("oo_alerts");
                alert.HasKey(a => a.Id);
                alert.Property(a => a.Id).HasMaxLength(36);
                alert.Property(a => a.OrganizationId).HasMaxLength(36).IsRequired();
                alert.Property(a => a.RuleId).HasMaxLength(36);
                alert.Property(a => a.RuleName).HasMaxLength(100);
                alert.Property(a => a.SiteId).HasMaxLength(36);
                alert.Property(a => a.CameraId).HasMaxLength(64);
                alert.Property(a => a.CameraName).HasMaxLength(100);
                alert.Property(a => a.EventId).HasMaxLength(36);
                alert.Property(a => a.Category).HasMaxLength(32);
                alert.Property(a => a.Severity).HasMaxLength(16);
                alert.Property(a => a.Title).HasMaxLength(200);
                alert.Property(a => a.AcknowledgedBy).HasMaxLength(100);
                alert.HasIndex(a => new { a.OrganizationId, a.TriggeredAt });
            });

            modelBuilder.Entity<GatewayRecord>(gateway =>
            {
                gateway.ToTable("oo_gateways");
                gateway.HasKey(g => g.Id);
                gateway.Property(g => g.Id).HasMaxLength(36);
                gateway.Property(g => g.OrganizationId).HasMaxLength(36).IsRequired();
                gateway.Property(g => g.Name).HasMaxLength(100).IsRequired();
                gateway.Property(g => g.Location).HasMaxLength(16).IsRequired();
                gateway.Property(g => g.Version).HasMaxLength(64);
                gateway.Property(g => g.BackupProvider).HasMaxLength(200);
                gateway.Property(g => g.BackupPrefix).HasMaxLength(200);
                gateway.Property(g => g.TokenHash).HasMaxLength(64);
                gateway.HasIndex(g => g.OrganizationId);
                gateway.HasIndex(g => g.TokenHash);
            });

            modelBuilder.Entity<PluginRecord>(plugin =>
            {
                plugin.ToTable("oo_plugins");
                plugin.HasKey(p => p.Key);
                plugin.Property(p => p.Key).HasMaxLength(255);
                plugin.Property(p => p.Folder).HasMaxLength(255).IsRequired();
                plugin.Property(p => p.Cameras).IsRequired();
            });
        }

        #endregion
    }

    /// <summary>A customer: everything else belongs to one. Its data stays in its region.</summary>
    public sealed class OrganizationRecord
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Data region chosen at sign-up (see <see cref="Tenancy.Regions"/>); it does not change.</summary>
        public string Region { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>A place with cameras; times there are shown in its time zone.</summary>
    public sealed class SiteRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string Name { get; set; }

        /// <summary>IANA time zone, e.g. "Asia/Kolkata".</summary>
        public string TimeZone { get; set; }

        public string Address { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>How cameras record and how long the video is kept; cameras refer to one.</summary>
    public sealed class RecordingPolicyRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string Name { get; set; }

        /// <summary>"Always", "EventsOnly" or "Schedule" (see <see cref="Tenancy.RecordingModes"/>).</summary>
        public string Mode { get; set; }

        public int PreRollSeconds { get; set; }
        public int PostRollSeconds { get; set; }

        /// <summary>Days continuous video is kept.</summary>
        public int ContinuousRetentionDays { get; set; }

        /// <summary>Days video around events is kept.</summary>
        public int EventRetentionDays { get; set; }

        /// <summary>The policy new cameras get unless another is chosen; one per organization.</summary>
        /// <summary>JSON: the weekly hours of a "Schedule" policy (see <see cref="Tenancy.ScheduleWindow"/>).</summary>
        public string Schedule { get; set; }

        public bool IsDefault { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A camera. Its id (a UUID) names its recordings on disk; its name is only a label.</summary>
    public sealed class CameraRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string SiteId { get; set; }
        public string PolicyId { get; set; }

        /// <summary>The gateway that records the camera.</summary>
        public string GatewayId { get; set; }

        public string Name { get; set; }
        public bool Enabled { get; set; }

        /// <summary>The connection and stream options as JSON, without the password.</summary>
        public string Options { get; set; }

        /// <summary>The password, encrypted (Data Protection); null without one.</summary>
        public string Password { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A person who signs in; what they may do comes from their memberships.</summary>
    public sealed class UserRecord
    {
        public string Id { get; set; }
        public string UserName { get; set; }

        /// <summary>Upper-cased user name: user names are case-insensitive.</summary>
        public string NormalizedUserName { get; set; }

        /// <summary>PBKDF2 hash (ASP.NET Core Identity password hasher).</summary>
        public string PasswordHash { get; set; }

        public bool Enabled { get; set; }

        /// <summary>Set for passwords an administrator chose: the user must choose a new one before anything else.</summary>
        public bool MustChangePassword { get; set; }

        /// <summary>Changes when the password, role, sites or state changes: tokens issued before become invalid.</summary>
        public string SecurityStamp { get; set; }

        public int FailedLogins { get; set; }
        public DateTime? LockedUntil { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A user's role in an organization, optionally limited to some of its sites.</summary>
    public sealed class MembershipRecord
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string OrganizationId { get; set; }

        /// <summary>See <see cref="Security.Roles"/>.</summary>
        public string Role { get; set; }

        /// <summary>The site ids the user may see, as a JSON array; empty: all sites.</summary>
        public string Sites { get; set; }

        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// A saved live view: which cameras show in which tiles of a grid. Private to its owner, or shared with the
    /// organization (no owner; managed by administrators).
    /// </summary>
    public sealed class LayoutRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }

        /// <summary>The user the layout belongs to; null: shared.</summary>
        public string OwnerId { get; set; }

        public string Name { get; set; }

        /// <summary>The grid template the web application draws, e.g. "2x2" or "1+5".</summary>
        public string Grid { get; set; }

        /// <summary>The camera of each tile as a JSON array; null for an empty tile.</summary>
        public string Cameras { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Something that happened, kept for as long as the camera's policy keeps video around events: motion, a person
    /// or vehicle in view, tampering, an input, the camera going offline, a plugin's finding. An event with a duration
    /// has an end time; one still going on has none yet.
    /// </summary>
    public sealed class EventRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string SiteId { get; set; }

        /// <summary>The camera it happened at; null for events about no camera (some plugin events).</summary>
        public string CameraId { get; set; }

        /// <summary>See <see cref="Events.EventCategories"/>.</summary>
        public string Category { get; set; }

        /// <summary>Info, Warning or Critical.</summary>
        public string Severity { get; set; }

        /// <summary>"camera", "detection", "recorder" or "plugin:&lt;name&gt;".</summary>
        public string Source { get; set; }

        /// <summary>A short readable name: "Motion", "Person", "Camera offline".</summary>
        public string Title { get; set; }

        /// <summary>The ONVIF topic or plugin event type it came from.</summary>
        public string Topic { get; set; }

        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }

        /// <summary>Details as a JSON object (e.g. object classes and likelihood, a plugin's data).</summary>
        public string Data { get; set; }
    }

    /// <summary>Who did what, when and from where: changes to settings, sign-ins, and watching or exporting video.</summary>
    public sealed class AuditRecord
    {
        public string Id { get; set; }

        /// <summary>Null for sign-ins of unknown user names.</summary>
        public string OrganizationId { get; set; }

        public string UserId { get; set; }
        public string UserName { get; set; }

        /// <summary>See <see cref="Audit.AuditActions"/>, e.g. "camera.update", "video.export".</summary>
        public string Action { get; set; }

        public string TargetType { get; set; }
        public string TargetId { get; set; }
        public string TargetName { get; set; }

        /// <summary>Details as a JSON object; never secrets.</summary>
        public string Details { get; set; }

        public string IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>A plugin folder row: <see cref="Key"/> is the lower-cased folder name.</summary>
    /// <summary>A gateway: what records cameras, its settings and its last heartbeat (see Gateways.Gateway).</summary>
    public sealed class GatewayRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public bool Embedded { get; set; }
        public string Version { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? LastSeenAt { get; set; }

        /// <summary>JSON: the last reported health.</summary>
        public string Health { get; set; }

        public double? MinFreeSpaceGB { get; set; }
        public double? MaxSizePerCameraGB { get; set; }
        public double? MaxTotalSizeGB { get; set; }
        public string BackupProvider { get; set; }
        public string BackupPrefix { get; set; }
        public int BackupKeepLocalCopyHours { get; set; }

        /// <summary>Encrypted JSON: the backup provider's settings.</summary>
        public string BackupSettings { get; set; }

        public bool DiscoveryMulticast { get; set; }

        /// <summary>JSON array of addresses probed directly.</summary>
        public string DiscoveryAddresses { get; set; }

        /// <summary>SHA-256 (hex) of a paired gateway's token.</summary>
        public string TokenHash { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A case: an incident being looked into, with the clips of video that show it.</summary>
    public sealed class CaseRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>"Open" (its video is kept) or "Closed".</summary>
        public string Status { get; set; }

        public string CreatedById { get; set; }
        public string CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? ClosedAt { get; set; }
    }

    /// <summary>A bookmark in a case: a stretch of time on one or more cameras of a site, with a note.</summary>
    public sealed class CaseClipRecord
    {
        public string Id { get; set; }
        public string CaseId { get; set; }
        public string OrganizationId { get; set; }
        public string SiteId { get; set; }

        /// <summary>JSON array of camera ids.</summary>
        public string Cameras { get; set; }

        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public string Note { get; set; }

        /// <summary>The event it was saved from, if any.</summary>
        public string EventId { get; set; }

        public string CreatedById { get; set; }
        public string CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>An MP4 export of one camera, watermarked; the file is kept until <see cref="ExpiresAt"/>.</summary>
    public sealed class ExportRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string SiteId { get; set; }
        public string CameraId { get; set; }
        public string CameraName { get; set; }
        public DateTime From { get; set; }
        public DateTime To { get; set; }

        /// <summary>The case it was exported for, if any.</summary>
        public string CaseId { get; set; }

        public string RequestedById { get; set; }
        public string RequestedByName { get; set; }

        /// <summary>Queued, Running, Ready, Failed or Expired.</summary>
        public string Status { get; set; }

        public string Error { get; set; }
        public long? Size { get; set; }

        /// <summary>SHA-256 (hex) of the file, to show it was not changed.</summary>
        public string Sha256 { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>A link that lets anyone holding it download an export until it expires or is revoked.</summary>
    public sealed class ShareLinkRecord
    {
        public string Id { get; set; }
        public string ExportId { get; set; }
        public string OrganizationId { get; set; }

        /// <summary>SHA-256 (hex) of the token in the link (the token itself is shown once).</summary>
        public string TokenHash { get; set; }

        public string CreatedByName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public int Downloads { get; set; }
    }

    /// <summary>A sign-up waiting for its email to be confirmed (cloud); the organization is created once it is.</summary>
    public sealed class SignupRecord
    {
        public string Id { get; set; }

        /// <summary>SHA-256 of the token in the emailed link (the token itself is never stored).</summary>
        public string TokenHash { get; set; }

        public string Email { get; set; }

        /// <summary>The password chosen, encrypted with a key that expires with the sign-up.</summary>
        public string Password { get; set; }

        public string OrganizationName { get; set; }
        public string SiteName { get; set; }
        public string TimeZone { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>When to raise an alert and whom to tell (see Alerts.AlertRule).</summary>
    public sealed class AlertRuleRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string Name { get; set; }
        public bool Enabled { get; set; }

        /// <summary>"Event" or "Offline".</summary>
        public string Kind { get; set; }

        /// <summary>JSON: the conditions and recipients (categories, cameras, sites, hours, emails, ...).</summary>
        public string Settings { get; set; }

        public string WebhookUrl { get; set; }

        /// <summary>Encrypted: the key webhooks are signed with.</summary>
        public string WebhookSecret { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>An alert in the inbox: what a rule caught, and whether someone has seen to it.</summary>
    public sealed class AlertRecord
    {
        public string Id { get; set; }
        public string OrganizationId { get; set; }
        public string RuleId { get; set; }
        public string RuleName { get; set; }
        public string SiteId { get; set; }
        public string CameraId { get; set; }
        public string CameraName { get; set; }
        public string EventId { get; set; }
        public string Category { get; set; }
        public string Severity { get; set; }
        public string Title { get; set; }
        public DateTime TriggeredAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string AcknowledgedBy { get; set; }

        /// <summary>JSON: how each notification went (email, webhook).</summary>
        public string Delivery { get; set; }
    }

    public sealed class PluginRecord
    {
        public string Key { get; set; }
        public string Folder { get; set; }
        public bool Enabled { get; set; }

        /// <summary>Camera ids as a JSON array; empty: all cameras.</summary>
        public string Cameras { get; set; }

        /// <summary>The settings JSON, encrypted (Data Protection); null without settings.</summary>
        public string Settings { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
