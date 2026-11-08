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
        public TestSettings()
        {
            Root = Path.Combine(Path.GetTempPath(), "onvif-open-settings", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Store = CreateStore(Database, Keys);
        }

        public string Root { get; }
        public string Database => Path.Combine(Root, "settings.db");
        public string Keys => Path.Combine(Root, "keys");
        public ISettingsStore Store { get; }

        public const string AdminName = "admin";
        public const string AdminPassword = "Test-Admin-Pass-1";

        /// <summary>Points the recorder at this database and these keys, with a known first admin.</summary>
        public void Apply(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={Database}");
            builder.UseSetting("DataProtection:KeysDirectory", Keys);
            builder.UseSetting("Security:InitialAdmin:UserName", AdminName);
            builder.UseSetting("Security:InitialAdmin:Password", AdminPassword);
            builder.UseSetting("Security:SignInsPerMinute", "1000");
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

        public TestSettings Camera(CameraOptions camera)
        {
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
            return new EfSettingsStore(contexts, DataProtectionProvider.Create(new DirectoryInfo(keys), b => b.SetApplicationName("Onvif.Open.Recorder")));
        }

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

        private static ISettingsStore CreateStore(string database, string keys)
        {
            return CreateStore("Sqlite", $"Data Source={database}", keys, out _);
        }
    }
}
