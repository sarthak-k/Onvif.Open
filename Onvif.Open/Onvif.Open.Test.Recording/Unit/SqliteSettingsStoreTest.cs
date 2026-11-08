// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class SqliteSettingsStoreTest
    {
        #region Private Methods

        private static SqliteSettingsStore Open(TempDirectory temp)
        {
            return new SqliteSettingsStore(Path.Combine(temp.Path, "db", "settings.db"), DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(temp.Path, "keys"))));
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

        [Fact]
        public void CamerasShouldRoundTripInOrderAndBeReplacedById()
        {
            using (var temp = new TempDirectory())
            {
                var store = Open(temp);
                store.SaveCamera(Camera("b-cam"));
                store.SaveCamera(Camera("a-cam"));

                var changed = Camera("B-CAM", password: "other");
                changed.Mode = Onvif.Open.Core.Recording.RecordingMode.Continuous;
                store.SaveCamera(changed);

                var cameras = Open(temp).GetCameras();
                Assert.Equal(new[] { "b-cam", "a-cam" }, cameras.Select(c => c.Id));
                Assert.Equal(Onvif.Open.Core.Recording.RecordingMode.Continuous, cameras[0].Mode);
                Assert.Equal("other", cameras[0].Password);
                Assert.Equal(new[] { "Human" }, cameras[1].Analytics.TriggerClasses);
                Assert.Equal("s3cret-pass", store.GetCamera("A-CAM").Password);

                Assert.True(store.DeleteCamera("a-cam"));
                Assert.False(store.DeleteCamera("a-cam"));
                Assert.Null(store.GetCamera("a-cam"));
            }
        }

        [Fact]
        public void SecretsShouldBeEncryptedAtRest()
        {
            using (var temp = new TempDirectory())
            {
                var store = Open(temp);
                store.SaveCamera(Camera("cam1"));
                store.SavePlugin(new StoredPlugin { Folder = "Mailer", Cameras = { "cam1" }, Settings = { ["Smtp:Password"] = "mail-secret" } });

                var raw = File.ReadAllBytes(store.DatabasePath);
                Assert.DoesNotContain("s3cret-pass", Encoding.UTF8.GetString(raw));
                Assert.DoesNotContain("mail-secret", Encoding.UTF8.GetString(raw));

                var plugin = Open(temp).GetPlugins().Single();
                Assert.Equal("mail-secret", plugin.Settings["smtp:password"]);
                Assert.Equal(new[] { "cam1" }, plugin.Cameras);
                Assert.True(plugin.Enabled);

                // Without the keys, the secrets cannot be read.
                var otherKeys = new SqliteSettingsStore(store.DatabasePath, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(temp.Path, "other-keys"))));
                Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => otherKeys.GetCameras());
            }
        }

        [Fact]
        public void ImportShouldBeRecordedAndMigrationsRunOnce()
        {
            using (var temp = new TempDirectory())
            {
                var store = Open(temp);
                Assert.False(store.IsImported);

                store.Import(new[] { Camera("cam1") }, new[] { new StoredPlugin { Folder = "Sample" } });

                var reopened = Open(temp);
                Assert.True(reopened.IsImported);
                Assert.Single(reopened.GetCameras());
                Assert.Single(reopened.GetPlugins());
            }
        }

        [Fact]
        public void DatabasesOfNewerVersionsShouldBeRefused()
        {
            using (var temp = new TempDirectory())
            {
                var store = Open(temp);

                using (var connection = new SqliteConnection($"Data Source={store.DatabasePath};Pooling=False"))
                {
                    connection.Open();
                    var command = connection.CreateCommand();
                    command.CommandText = "UPDATE meta SET value = '99' WHERE key = 'schema_version'";
                    command.ExecuteNonQuery();
                }

                var error = Assert.Throws<InvalidOperationException>(() => Open(temp));
                Assert.Contains("newer version", error.Message);
            }
        }

        #endregion
    }
}
