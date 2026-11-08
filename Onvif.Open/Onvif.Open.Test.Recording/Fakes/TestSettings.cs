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

        /// <summary>Points the recorder at this database and these keys.</summary>
        public void Apply(IWebHostBuilder builder)
        {
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={Database}");
            builder.UseSetting("DataProtection:KeysDirectory", Keys);
        }

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
