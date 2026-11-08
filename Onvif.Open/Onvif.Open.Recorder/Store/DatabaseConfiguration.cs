// Sarthak Killedar
// 2026-11-08

using System;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// "Database" section: which database keeps the settings.
    /// <code>
    /// "Database": { "Provider": "PostgreSql", "ConnectionString": "Host=db;Database=onvif;Username=onvif;Password=..." }
    /// </code>
    /// Providers: Sqlite (default: data/onvif-open.db), SqlServer, PostgreSql, MySql.
    /// </summary>
    public static class DatabaseConfiguration
    {
        #region Public Variables

        public const string SectionName = "Database";
        public const string DefaultSqlite = "Data Source=data/onvif-open.db";

        #endregion

        #region Public Methods

        /// <summary>Registers the settings database (a <see cref="RecorderDbContext"/> factory) and <see cref="ISettingsStore"/>.</summary>
        public static IServiceCollection AddSettingsDatabase(this IServiceCollection services, IConfiguration configuration, string contentRoot)
        {
            var section = configuration.GetSection(SectionName);
            var provider = (section["Provider"] ?? "Sqlite").Trim();
            var connectionString = section["ConnectionString"];

            services.AddDbContextFactory<RecorderDbContext>(options => Configure(options, provider, connectionString, contentRoot));
            services.AddSingleton<ISettingsStore, EfSettingsStore>();
            return services;
        }

        /// <summary>Applies the provider to EF Core options (also used by tests).</summary>
        public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string provider, string connectionString, string contentRoot)
        {
            switch (provider.ToLowerInvariant())
            {
                case "sqlite":
                    return options.UseSqlite(SqliteConnectionString(connectionString ?? DefaultSqlite, contentRoot));
                case "sqlserver":
                    return options.UseSqlServer(Require(connectionString, provider));
                case "postgresql":
                case "postgres":
                case "npgsql":
                    return options.UseNpgsql(Require(connectionString, provider));
                case "mysql":
                    return options.UseMySQL(Require(connectionString, provider));
                default:
                    throw new InvalidOperationException($"Unknown Database:Provider '{provider}'. Use Sqlite, SqlServer, PostgreSql or MySql.");
            }
        }

        /// <summary>
        /// Creates the database (when allowed and missing) and the recorder's tables (when missing), also inside an
        /// existing database that holds other tables.
        /// </summary>
        public static async Task EnsureCreatedAsync(IDbContextFactory<RecorderDbContext> contexts, CancellationToken cancellationToken = default)
        {
            await using (var db = await contexts.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var creator = db.GetService<IRelationalDatabaseCreator>();

                if (!await creator.ExistsAsync(cancellationToken).ConfigureAwait(false))
                {
                    await creator.CreateAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!await TablesExistAsync(db, cancellationToken).ConfigureAwait(false))
                {
                    await creator.CreateTablesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private Methods

        private static async Task<bool> TablesExistAsync(RecorderDbContext db, CancellationToken cancellationToken)
        {
            try
            {
                await db.Cameras.AnyAsync(cancellationToken).ConfigureAwait(false);
                await db.Plugins.AnyAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (DbException)
            {
                return false;
            }
        }

        /// <summary>A relative SQLite file is resolved against the content root, and its folder created.</summary>
        private static string SqliteConnectionString(string connectionString, string contentRoot)
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);

            if (!string.IsNullOrEmpty(builder.DataSource) && builder.DataSource != ":memory:" && !builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                builder.DataSource = Path.GetFullPath(Path.Combine(contentRoot, builder.DataSource));
                Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource));
            }

            return builder.ToString();
        }

        private static string Require(string connectionString, string provider)
        {
            return string.IsNullOrWhiteSpace(connectionString)
                ? throw new InvalidOperationException($"Database:ConnectionString is required for the {provider} provider.")
                : connectionString;
        }

        #endregion
    }
}
