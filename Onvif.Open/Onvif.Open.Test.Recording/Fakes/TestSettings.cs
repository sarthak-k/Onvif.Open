// Sarthak Killedar
// 2026-11-08

using System;
using System.IO;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Store;

namespace Onvif.Open.Test.Recording.Fakes
{
    /// <summary>
    /// A temporary settings database (SQLite) and Data Protection keys for a recorder under test; cameras and plugins
    /// are seeded through the real store before the host starts, as the API would have saved them.
    /// </summary>
    internal sealed class TestSettings : IDisposable
    {
        /// <param name="seeded">False: an empty database, as before the first-run setup.</param>
        public TestSettings(bool seeded = true)
        {
            Root = Path.Combine(Path.GetTempPath(), "onvif-open-settings", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Store = CreateStore("Sqlite", $"Data Source={Database}", Keys, out var contexts);
            Contexts = contexts;
            Tenancy = new Onvif.Open.Recorder.Tenancy.EfTenancyStore(contexts);
            Users = new EfUserStore(contexts);
            Gateways = new Onvif.Open.Recorder.Gateways.EfGatewayStore(contexts, Protection(Keys));

            if (seeded)
            {
                SeedTenancy();
            }
        }

        // The organization a seeded recorder starts with: one site, the two default policies and the owner "admin".
        public const string OrganizationId = "00000000-0000-0000-0000-00000000a001";
        public const string SiteId = "00000000-0000-0000-0000-00000000b001";
        public const string AlwaysPolicyId = "00000000-0000-0000-0000-00000000c001";
        public const string EventsPolicyId = "00000000-0000-0000-0000-00000000c002";
        public const string GatewayId = "00000000-0000-0000-0000-00000000e001";

        /// <summary>The database itself, to check what is stored.</summary>
        public IDbContextFactory<RecorderDbContext> Contexts { get; }

        public Onvif.Open.Recorder.Tenancy.ITenancyStore Tenancy { get; }
        public IUserStore Users { get; }
        public Onvif.Open.Recorder.Gateways.IGatewayStore Gateways { get; }

        /// <summary>Changes the seeded gateway's settings before the recorder starts.</summary>
        public TestSettings Gateway(Action<Onvif.Open.Recorder.Gateways.Gateway> change)
        {
            var gateway = Gateways.FindAsync(GatewayId).GetAwaiter().GetResult();
            change(gateway);
            Gateways.SaveAsync(gateway).GetAwaiter().GetResult();
            return this;
        }

        /// <summary>A second site of the organization, for site access tests.</summary>
        public string AddSite(string name)
        {
            var id = Guid.NewGuid().ToString("D");
            Tenancy.SaveSiteAsync(new SiteRecord { Id = id, OrganizationId = OrganizationId, Name = name, TimeZone = "UTC", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }).GetAwaiter().GetResult();
            return id;
        }

        private void SeedTenancy()
        {
            var now = DateTime.UtcNow;
            Tenancy.SaveOrganizationAsync(new OrganizationRecord { Id = OrganizationId, Name = "Test organization", Region = "us", CreatedAt = now }).GetAwaiter().GetResult();
            Tenancy.SaveSiteAsync(new SiteRecord { Id = SiteId, OrganizationId = OrganizationId, Name = "Main site", TimeZone = "UTC", CreatedAt = now, UpdatedAt = now }).GetAwaiter().GetResult();
            Tenancy.SavePolicyAsync(new RecordingPolicyRecord { Id = AlwaysPolicyId, OrganizationId = OrganizationId, Name = "Always record", Mode = "Always", PreRollSeconds = 5, PostRollSeconds = 10, ContinuousRetentionDays = 7, EventRetentionDays = 30, IsDefault = true, CreatedAt = now, UpdatedAt = now }).GetAwaiter().GetResult();
            Tenancy.SavePolicyAsync(new RecordingPolicyRecord { Id = EventsPolicyId, OrganizationId = OrganizationId, Name = "Events only", Mode = "EventsOnly", PreRollSeconds = 5, PostRollSeconds = 10, ContinuousRetentionDays = 7, EventRetentionDays = 30, CreatedAt = now, UpdatedAt = now }).GetAwaiter().GetResult();

            // The recorder's own gateway; no free-space limit, so test machines with little disk keep their recordings.
            Gateways.SaveAsync(new Onvif.Open.Recorder.Gateways.Gateway
            {
                Id = GatewayId,
                OrganizationId = OrganizationId,
                Name = "Test gateway",
                Embedded = true,
                Storage = new Onvif.Open.Recorder.Gateways.StorageLimits { MinFreeSpaceGB = null },
                CreatedAt = now,
                UpdatedAt = now
            }).GetAwaiter().GetResult();

            var owner = new UserRecord { Id = "00000000-0000-0000-0000-00000000d001", UserName = AdminName, Enabled = true, SecurityStamp = Guid.NewGuid().ToString("N"), CreatedAt = now };
            owner.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<UserRecord>().HashPassword(owner, AdminPassword);
            Users.SaveAsync(owner).GetAwaiter().GetResult();
            Users.SaveMembershipAsync(new MembershipRecord { Id = Guid.NewGuid().ToString("D"), UserId = owner.Id, OrganizationId = OrganizationId, Role = "Owner", Sites = "[]", CreatedAt = now }).GetAwaiter().GetResult();
        }

        public string Root { get; }
        public string Database => Path.Combine(Root, "settings.db");
        public string Keys => Path.Combine(Root, "keys");
        public ISettingsStore Store { get; }

        public const string AdminName = "admin";
        public const string AdminPassword = "Test-Admin-Pass-1";

        /// <summary>Points the recorder at this database (already set up, with the owner "admin") and these keys.</summary>
        public void Apply(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={Database}");
            builder.UseSetting("DataProtection:KeysDirectory", Keys);
            builder.UseSetting("Security:SignInsPerMinute", "1000");

            // Every SQL statement at Information drowns test failures in the CI log.
            builder.UseSetting("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Warning");
            builder.UseSetting("Logging:LogLevel:Microsoft.AspNetCore", "Warning");
        }

        /// <summary>Signs <paramref name="client"/> in (as the admin by default) and returns it.</summary>
        public static System.Net.Http.HttpClient Authenticate(System.Net.Http.HttpClient client, string userName = AdminName, string password = AdminPassword)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", SignInAsync(client, userName, password).GetAwaiter().GetResult().AccessToken);
            return client;
        }

        public static async System.Threading.Tasks.Task<Tokens> SignInAsync(System.Net.Http.HttpClient client, string userName = AdminName, string password = AdminPassword)
        {
            var response = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, "/api/auth/login", new { userName, password });
            response.EnsureSuccessStatusCode();
            var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
            return new Tokens(json.GetProperty("accessToken").GetString(), json.GetProperty("refreshToken").GetString());
        }

        public sealed record Tokens(string AccessToken, string RefreshToken);

        /// <summary>Seeds a camera; by default at the main site, recording always (or only on events in triggered mode).</summary>
        public TestSettings Camera(CameraOptions camera)
        {
            camera.OrganizationId ??= OrganizationId;
            camera.SiteId ??= SiteId;
            camera.PolicyId ??= camera.Mode == Onvif.Open.Core.Recording.RecordingMode.Triggered ? EventsPolicyId : AlwaysPolicyId;
            camera.Name ??= camera.Id;
            camera.GatewayId ??= GatewayId;
            Store.SaveCameraAsync(camera).GetAwaiter().GetResult();
            return this;
        }

        public TestSettings Plugin(StoredPlugin plugin)
        {
            Store.SavePluginAsync(plugin).GetAwaiter().GetResult();
            return this;
        }

        /// <summary>A store on any EF Core provider, with keys the recorder can read (same application name).</summary>
        public static ISettingsStore CreateStore(string provider, string connectionString, string keys, out IDbContextFactory<RecorderDbContext> contexts)
        {
            var options = DatabaseConfiguration.Configure(new DbContextOptionsBuilder<RecorderDbContext>(), provider, connectionString, Path.GetTempPath());
            contexts = new PooledDbContextFactory<RecorderDbContext>((DbContextOptions<RecorderDbContext>)options.Options);
            DatabaseConfiguration.EnsureCreatedAsync(contexts).GetAwaiter().GetResult();
            return new EfSettingsStore(contexts, Protection(keys));
        }

        private static IDataProtectionProvider Protection(string keys) =>
            DataProtectionProvider.Create(new DirectoryInfo(keys), b => b.SetApplicationName("Onvif.Open.Recorder"));

        public void Dispose()
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(Root, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }


    }
}
