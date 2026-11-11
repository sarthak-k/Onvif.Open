// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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
        /// existing database that holds other tables. A database from an earlier version gets the tables added since.
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

                var missing = await MissingTablesAsync(db, cancellationToken).ConfigureAwait(false);

                if (missing.Count == db.Model.GetEntityTypes().Count())
                {
                    await creator.CreateTablesAsync(cancellationToken).ConfigureAwait(false);
                }
                else if (missing.Count > 0)
                {
                    await CreateTablesAsync(db, missing, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>The recorder's tables the database does not have (a query on each tells).</summary>
        private static async Task<List<string>> MissingTablesAsync(RecorderDbContext db, CancellationToken cancellationToken)
        {
            var probes = new (string Table, Func<CancellationToken, Task<bool>> Probe)[]
            {
                ("oo_organizations", ct => db.Organizations.AnyAsync(ct)),
                ("oo_sites", ct => db.Sites.AnyAsync(ct)),
                ("oo_recording_policies", ct => db.RecordingPolicies.AnyAsync(ct)),
                ("oo_cameras", ct => db.Cameras.AnyAsync(ct)),
                ("oo_users", ct => db.Users.AnyAsync(ct)),
                ("oo_memberships", ct => db.Memberships.AnyAsync(ct)),
                ("oo_layouts", ct => db.Layouts.AnyAsync(ct)),
                ("oo_plugins", ct => db.Plugins.AnyAsync(ct)),
                ("oo_events", ct => db.Events.AnyAsync(ct)),
                ("oo_audit", ct => db.Audit.AnyAsync(ct)),
                ("oo_gateways", ct => db.Gateways.AnyAsync(ct)),
                ("oo_cases", ct => db.Cases.AnyAsync(ct)),
                ("oo_case_clips", ct => db.CaseClips.AnyAsync(ct)),
                ("oo_exports", ct => db.Exports.AnyAsync(ct)),
                ("oo_share_links", ct => db.ShareLinks.AnyAsync(ct)),
                ("oo_alert_rules", ct => db.AlertRules.AnyAsync(ct)),
                ("oo_alerts", ct => db.Alerts.AnyAsync(ct)),
                ("oo_signups", ct => db.Signups.AnyAsync(ct)),
                ("oo_sso", ct => db.SingleSignOn.AnyAsync(ct)),
                ("oo_sso_domains", ct => db.SsoDomains.AnyAsync(ct)),
                ("oo_external_logins", ct => db.ExternalLogins.AnyAsync(ct)),
                ("oo_plans", ct => db.Plans.AnyAsync(ct))
            };
            var missing = new List<string>();

            foreach (var (table, probe) in probes)
            {
                try
                {
                    await probe(cancellationToken).ConfigureAwait(false);
                }
                catch (DbException)
                {
                    missing.Add(table);
                }
            }

            return missing;
        }

        /// <summary>
        /// Creates only the given tables (and their indexes) with the provider's own SQL: what CreateTables does for
        /// them, without touching the tables that exist.
        /// </summary>
        private static async Task CreateTablesAsync(RecorderDbContext db, IReadOnlyCollection<string> tables, CancellationToken cancellationToken)
        {
            var model = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
            var operations = db.GetService<IMigrationsModelDiffer>()
                .GetDifferences(null, model)
                .Where(o => o switch
                {
                    CreateTableOperation create => tables.Contains(create.Name),
                    CreateIndexOperation index => tables.Contains(index.Table),
                    _ => false
                })
                .ToList();

            var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model);
            await db.GetService<IMigrationCommandExecutor>()
                .ExecuteNonQueryAsync(commands, db.GetService<IRelationalConnection>(), cancellationToken)
                .ConfigureAwait(false);
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
