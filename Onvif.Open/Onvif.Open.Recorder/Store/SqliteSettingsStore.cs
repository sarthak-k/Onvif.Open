// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Onvif.Open.Recorder.Configuration;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// <see cref="ISettingsStore"/> in a SQLite file. Camera passwords and plugin settings are encrypted with ASP.NET Core
    /// Data Protection (keys next to the database); everything else is stored as JSON. The schema is versioned and
    /// migrated on open.
    /// </summary>
    public sealed class SqliteSettingsStore : ISettingsStore
    {
        #region Constructor

        public SqliteSettingsStore(string databasePath, IDataProtectionProvider dataProtection)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
            {
                throw new ArgumentNullException(nameof(databasePath));
            }

            DatabasePath = Path.GetFullPath(databasePath);
            Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath));
            _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString();
            _passwords = (dataProtection ?? throw new ArgumentNullException(nameof(dataProtection))).CreateProtector("Onvif.Open.Recorder.CameraPassword.v1");
            _pluginSettings = dataProtection.CreateProtector("Onvif.Open.Recorder.PluginSettings.v1");
            Migrate();
        }

        #endregion

        #region Private Variables

        /// <summary>Schema migrations, applied in order; never change one that has shipped, add a new one.</summary>
        private static readonly string[] Migrations =
        {
            @"CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
              CREATE TABLE cameras (id TEXT PRIMARY KEY COLLATE NOCASE, position INTEGER NOT NULL, options TEXT NOT NULL, password TEXT, updated_at TEXT NOT NULL);
              CREATE TABLE plugins (folder TEXT PRIMARY KEY COLLATE NOCASE, options TEXT NOT NULL, settings TEXT, updated_at TEXT NOT NULL);"
        };

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            Converters = { new JsonStringEnumConverter() },
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _connectionString;
        private readonly IDataProtector _passwords;
        private readonly IDataProtector _pluginSettings;
        private readonly object _sync = new object();

        #endregion

        #region Public Variables

        public string DatabasePath { get; }

        public bool IsImported => Execute(connection => Scalar(connection, "SELECT value FROM meta WHERE key = 'imported'") != null);

        #endregion

        #region Public Methods

        public IReadOnlyList<CameraOptions> GetCameras()
        {
            return Execute(connection =>
            {
                var cameras = new List<CameraOptions>();

                using (var command = Command(connection, "SELECT id, options, password FROM cameras ORDER BY position, id"))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cameras.Add(ToCamera(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
                    }
                }

                return (IReadOnlyList<CameraOptions>)cameras;
            });
        }

        public CameraOptions GetCamera(string cameraId)
        {
            return Execute(connection =>
            {
                using (var command = Command(connection, "SELECT id, options, password FROM cameras WHERE id = $id", ("$id", cameraId)))
                using (var reader = command.ExecuteReader())
                {
                    return reader.Read() ? ToCamera(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)) : null;
                }
            });
        }

        public void SaveCamera(CameraOptions camera)
        {
            Execute(connection =>
            {
                WriteCamera(connection, null, camera);
                return true;
            });
        }

        public bool DeleteCamera(string cameraId)
        {
            return Execute(connection =>
            {
                using (var command = Command(connection, "DELETE FROM cameras WHERE id = $id", ("$id", cameraId)))
                {
                    return command.ExecuteNonQuery() > 0;
                }
            });
        }

        public IReadOnlyList<StoredPlugin> GetPlugins()
        {
            return Execute(connection =>
            {
                var plugins = new List<StoredPlugin>();

                using (var command = Command(connection, "SELECT folder, options, settings FROM plugins ORDER BY folder"))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var plugin = JsonSerializer.Deserialize<StoredPlugin>(reader.GetString(1), Json) ?? new StoredPlugin();
                        plugin.Folder = reader.GetString(0);
                        plugin.Settings = reader.IsDBNull(2)
                            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            : new Dictionary<string, string>(JsonSerializer.Deserialize<Dictionary<string, string>>(_pluginSettings.Unprotect(reader.GetString(2)), Json), StringComparer.OrdinalIgnoreCase);
                        plugins.Add(plugin);
                    }
                }

                return (IReadOnlyList<StoredPlugin>)plugins;
            });
        }

        public void SavePlugin(StoredPlugin plugin)
        {
            Execute(connection =>
            {
                WritePlugin(connection, null, plugin);
                return true;
            });
        }

        public void Import(IEnumerable<CameraOptions> cameras, IEnumerable<StoredPlugin> plugins)
        {
            Execute(connection =>
            {
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var camera in cameras ?? Enumerable.Empty<CameraOptions>())
                    {
                        WriteCamera(connection, transaction, camera);
                    }

                    foreach (var plugin in plugins ?? Enumerable.Empty<StoredPlugin>())
                    {
                        WritePlugin(connection, transaction, plugin);
                    }

                    using (var command = Command(connection, "INSERT OR REPLACE INTO meta (key, value) VALUES ('imported', $now)", ("$now", Now())))
                    {
                        command.Transaction = transaction;
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }

                return true;
            });
        }

        #endregion

        #region Private Methods

        private void Migrate()
        {
            Execute(connection =>
            {
                var version = 0;
                using (var exists = Command(connection, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name = 'meta'"))
                {
                    if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
                    {
                        version = int.Parse(Scalar(connection, "SELECT value FROM meta WHERE key = 'schema_version'") ?? "0", CultureInfo.InvariantCulture);
                    }
                }

                if (version > Migrations.Length)
                {
                    throw new InvalidOperationException($"The settings database '{DatabasePath}' was created by a newer version (schema {version}).");
                }

                for (; version < Migrations.Length; version++)
                {
                    using (var transaction = connection.BeginTransaction())
                    {
                        using (var migration = Command(connection, Migrations[version]))
                        {
                            migration.Transaction = transaction;
                            migration.ExecuteNonQuery();
                        }

                        using (var update = Command(connection, "INSERT OR REPLACE INTO meta (key, value) VALUES ('schema_version', $version)", ("$version", (version + 1).ToString(CultureInfo.InvariantCulture))))
                        {
                            update.Transaction = transaction;
                            update.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                }

                return true;
            });
        }

        private void WriteCamera(SqliteConnection connection, SqliteTransaction transaction, CameraOptions camera)
        {
            // The password is kept apart, encrypted; the options never contain it.
            var password = string.IsNullOrEmpty(camera.Password) ? null : _passwords.Protect(camera.Password);
            var options = JsonSerializer.Serialize(WithoutPassword(camera), Json);

            using (var command = Command(connection,
                @"INSERT INTO cameras (id, position, options, password, updated_at)
                  VALUES ($id, (SELECT coalesce(max(position), 0) + 1 FROM cameras), $options, $password, $now)
                  ON CONFLICT(id) DO UPDATE SET options = excluded.options, password = excluded.password, updated_at = excluded.updated_at",
                ("$id", camera.Id), ("$options", options), ("$password", (object)password ?? DBNull.Value), ("$now", Now())))
            {
                command.Transaction = transaction;
                command.ExecuteNonQuery();
            }
        }

        private void WritePlugin(SqliteConnection connection, SqliteTransaction transaction, StoredPlugin plugin)
        {
            var options = JsonSerializer.Serialize(new StoredPlugin { Enabled = plugin.Enabled, Cameras = plugin.Cameras ?? new List<string>(), Settings = null }, Json);
            var settings = plugin.Settings == null || plugin.Settings.Count == 0 ? null : _pluginSettings.Protect(JsonSerializer.Serialize(plugin.Settings, Json));

            using (var command = Command(connection,
                @"INSERT INTO plugins (folder, options, settings, updated_at) VALUES ($folder, $options, $settings, $now)
                  ON CONFLICT(folder) DO UPDATE SET options = excluded.options, settings = excluded.settings, updated_at = excluded.updated_at",
                ("$folder", plugin.Folder), ("$options", options), ("$settings", (object)settings ?? DBNull.Value), ("$now", Now())))
            {
                command.Transaction = transaction;
                command.ExecuteNonQuery();
            }
        }

        private CameraOptions ToCamera(string id, string options, string password)
        {
            // The row key is the camera id (its first spelling): replacing a camera never renames it.
            var camera = JsonSerializer.Deserialize<CameraOptions>(options, Json);
            camera.Id = id;
            camera.Password = password == null ? null : _passwords.Unprotect(password);
            return camera;
        }

        private static CameraOptions WithoutPassword(CameraOptions camera)
        {
            var copy = JsonSerializer.Deserialize<CameraOptions>(JsonSerializer.Serialize(camera, Json), Json);
            copy.Password = null;
            return copy;
        }

        private T Execute<T>(Func<SqliteConnection, T> action)
        {
            // One writer at a time; SQLite serializes anyway, this avoids "database is locked" between our own threads.
            lock (_sync)
            {
                using (var connection = new SqliteConnection(_connectionString))
                {
                    connection.Open();
                    return action(connection);
                }
            }
        }

        private static SqliteCommand Command(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            return command;
        }

        private static string Scalar(SqliteConnection connection, string sql)
        {
            using (var command = Command(connection, sql))
            {
                return command.ExecuteScalar() as string;
            }
        }

        private static string Now()
        {
            return DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
