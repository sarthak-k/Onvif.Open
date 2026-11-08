// Sarthak Killedar
// 2026-11-08

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Integration
{
    /// <summary>
    /// The settings store on every supported database. SQLite always runs; the others run where CI provides them:
    ///   ONVIF_OPEN_TEST_POSTGRES  e.g. Host=127.0.0.1;Username=postgres;Password=...;Database=onvif_test
    ///   ONVIF_OPEN_TEST_SQLSERVER e.g. Server=127.0.0.1;User Id=sa;Password=...;TrustServerCertificate=true;Database=onvif_test
    ///   ONVIF_OPEN_TEST_MYSQL     e.g. Server=127.0.0.1;User=root;Password=...;Database=onvif_test
    /// </summary>
    public sealed class SettingsStoreTest : IDisposable
    {
        #region Private Variables

        private readonly string _root = Path.Combine(Path.GetTempPath(), "onvif-open-settings", Guid.NewGuid().ToString("N"));

        #endregion

        #region Private Methods

        private (ISettingsStore Store, IDbContextFactory<RecorderDbContext> Contexts) Open(string provider, string keys = "keys")
        {
            string connectionString;

            if (provider == "Sqlite")
            {
                connectionString = $"Data Source={Path.Combine(_root, "settings.db")}";
            }
            else
            {
                var variable = "ONVIF_OPEN_TEST_" + provider.ToUpperInvariant();
                connectionString = Environment.GetEnvironmentVariable(variable);
                Skip.If(string.IsNullOrWhiteSpace(connectionString), $"{variable} is not set.");
            }

            var store = TestSettings.CreateStore(provider, connectionString, Path.Combine(_root, keys), out var contexts);
            return (store, contexts);
        }

        /// <summary>Server databases are shared by the test runs: start from empty tables.</summary>
        private static async Task ClearAsync(IDbContextFactory<RecorderDbContext> contexts)
        {
            await using (var db = await contexts.CreateDbContextAsync())
            {
                await db.Cameras.ExecuteDeleteAsync();
                await db.Plugins.ExecuteDeleteAsync();

                try
                {
                    await db.Layouts.ExecuteDeleteAsync();
                }
                catch (System.Data.Common.DbException)
                {
                    // A database from before layouts: EnsureCreated adds the table.
                }
            }
        }

        private static CameraOptions Camera(string id, string password = "s3cret-pass") => new CameraOptions
        {
            Id = id,
            OnvifUrl = "http://192.168.1.10",
            Username = "admin",
            Password = password,
            Mode = Onvif.Open.Core.Recording.RecordingMode.Triggered,
            Analytics = new CameraAnalyticsOptions { TriggerClasses = { "Human" } }
        };

        #endregion

        #region Public Methods

        [SkippableTheory]
        [InlineData("Sqlite")]
        [InlineData("Postgres")]
        [InlineData("SqlServer")]
        [InlineData("MySql")]
        public async Task CamerasShouldRoundTripInOrderAndBeReplacedById(string provider)
        {
            var (store, contexts) = Open(provider);
            await ClearAsync(contexts);

            await store.SaveCameraAsync(Camera("b-cam"));
            await Task.Delay(10);
            await store.SaveCameraAsync(Camera("a-cam"));

            var changed = Camera("B-CAM", password: "other");
            changed.Mode = Onvif.Open.Core.Recording.RecordingMode.Continuous;
            changed.Enabled = false;
            await store.SaveCameraAsync(changed);

            var cameras = await store.GetCamerasAsync();
            Assert.Equal(new[] { "b-cam", "a-cam" }, cameras.Select(c => c.Id));
            Assert.Equal(Onvif.Open.Core.Recording.RecordingMode.Continuous, cameras[0].Mode);
            Assert.False(cameras[0].Enabled);
            Assert.Equal("other", cameras[0].Password);
            Assert.Equal(new[] { "Human" }, cameras[1].Analytics.TriggerClasses);
            Assert.Equal("s3cret-pass", (await store.GetCameraAsync("A-CAM")).Password);

            Assert.True(await store.DeleteCameraAsync("a-cam"));
            Assert.False(await store.DeleteCameraAsync("a-cam"));
            Assert.Null(await store.GetCameraAsync("a-cam"));
        }

        [SkippableTheory]
        [InlineData("Sqlite")]
        [InlineData("Postgres")]
        [InlineData("SqlServer")]
        [InlineData("MySql")]
        public async Task PluginsShouldRoundTrip(string provider)
        {
            var (store, contexts) = Open(provider);
            await ClearAsync(contexts);

            await store.SavePluginAsync(new StoredPlugin { Folder = "Mailer", Cameras = { "cam1" }, Settings = { ["Smtp:Password"] = "mail-secret" } });
            await store.SavePluginAsync(new StoredPlugin { Folder = "MAILER", Enabled = false, Cameras = { "cam1", "cam2" }, Settings = { ["Smtp:Password"] = "changed" } });
            await store.SavePluginAsync(new StoredPlugin { Folder = "Counter" });

            var plugins = await store.GetPluginsAsync();
            Assert.Equal(new[] { "Counter", "Mailer" }, plugins.Select(p => p.Folder));
            Assert.Empty(plugins[0].Settings);
            Assert.False(plugins[1].Enabled);
            Assert.Equal(new[] { "cam1", "cam2" }, plugins[1].Cameras);
            Assert.Equal("changed", plugins[1].Settings["smtp:password"]);

            Assert.True(await store.DeletePluginAsync("mailer"));
            Assert.Single(await store.GetPluginsAsync());
        }

        [Fact]
        public async Task SecretsShouldBeEncryptedAtRest()
        {
            var (store, _) = Open("Sqlite");
            await store.SaveCameraAsync(Camera("cam1"));
            await store.SavePluginAsync(new StoredPlugin { Folder = "Mailer", Settings = { ["Smtp:Password"] = "mail-secret" } });
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            var raw = Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_root, "settings.db")));
            Assert.DoesNotContain("s3cret-pass", raw);
            Assert.DoesNotContain("mail-secret", raw);
            Assert.Contains("onvif_cameras", raw);

            // Without the keys, the secrets cannot be read.
            var (otherKeys, _) = Open("Sqlite", keys: "other-keys");
            await Assert.ThrowsAnyAsync<System.Security.Cryptography.CryptographicException>(() => otherKeys.GetCamerasAsync());
        }

        [SkippableTheory]
        [InlineData("Sqlite")]
        [InlineData("Postgres")]
        [InlineData("SqlServer")]
        [InlineData("MySql")]
        public async Task TablesAddedSinceShouldBeCreatedInAnExistingDatabase(string provider)
        {
            var (store, contexts) = Open(provider);
            await ClearAsync(contexts);
            await store.SaveCameraAsync(Camera("kept"));

            // A database from before layouts existed.
            await using (var db = await contexts.CreateDbContextAsync())
            {
                await db.Database.ExecuteSqlRawAsync("DROP TABLE onvif_layouts");
            }

            await DatabaseConfiguration.EnsureCreatedAsync(contexts);

            var layouts = new EfLayoutStore(contexts);
            await layouts.SaveAsync(new LayoutRecord { Id = Guid.NewGuid().ToString(), Name = "Lobby", Grid = "2x2", Cameras = "[\"kept\"]", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });

            Assert.Equal("Lobby", Assert.Single(await layouts.GetVisibleAsync("anyone")).Name);
            Assert.Equal("kept", Assert.Single(await store.GetCamerasAsync()).Id);
        }

        [Fact]
        public async Task TablesShouldBeCreatedInADatabaseThatHasOtherTables()
        {
            var path = Path.Combine(_root, "shared.db");
            Directory.CreateDirectory(_root);

            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE someone_elses (id INTEGER PRIMARY KEY)";
                command.ExecuteNonQuery();
            }

            var store = TestSettings.CreateStore("Sqlite", $"Data Source={path}", Path.Combine(_root, "keys"), out _);
            await store.SaveCameraAsync(Camera("cam1"));
            Assert.Single(await store.GetCamerasAsync());
        }

        [Fact]
        public void UnknownProvidersShouldBeReported()
        {
            var error = Assert.Throws<InvalidOperationException>(() => DatabaseConfiguration.Configure(new DbContextOptionsBuilder<RecorderDbContext>(), "Oracle", "x", _root));
            Assert.Contains("Sqlite, SqlServer, PostgreSql or MySql", error.Message);
            Assert.Throws<InvalidOperationException>(() => DatabaseConfiguration.Configure(new DbContextOptionsBuilder<RecorderDbContext>(), "PostgreSql", null, _root));
        }

        public void Dispose()
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(_root, true);
            }
            catch (IOException)
            {
            }
        }

        #endregion
    }
}
