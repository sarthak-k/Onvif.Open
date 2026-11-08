// Sarthak Killedar
// 2026-11-08

using System;
using Microsoft.EntityFrameworkCore;

namespace Onvif.Open.Recorder.Store
{
    /// <summary>
    /// The settings database (EF Core): works on SQLite, SQL Server, PostgreSQL and MySQL. Camera options are one JSON
    /// column, so adding an option needs no schema change; tables are prefixed "onvif_" so they can live in a shared
    /// database.
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

        public DbSet<CameraRecord> Cameras => Set<CameraRecord>();
        public DbSet<PluginRecord> Plugins => Set<PluginRecord>();

        #endregion

        #region Protected Methods

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CameraRecord>(camera =>
            {
                camera.ToTable("onvif_cameras");
                camera.HasKey(c => c.Key);
                camera.Property(c => c.Key).HasMaxLength(64);
                camera.Property(c => c.Id).HasMaxLength(64).IsRequired();
                camera.Property(c => c.Options).IsRequired();
                camera.HasIndex(c => c.CreatedAt);
            });

            modelBuilder.Entity<PluginRecord>(plugin =>
            {
                plugin.ToTable("onvif_plugins");
                plugin.HasKey(p => p.Key);
                plugin.Property(p => p.Key).HasMaxLength(255);
                plugin.Property(p => p.Folder).HasMaxLength(255).IsRequired();
                plugin.Property(p => p.Cameras).IsRequired();
            });
        }

        #endregion
    }

    /// <summary>A camera row: <see cref="Key"/> is the lower-cased id (ids are case-insensitive).</summary>
    public sealed class CameraRecord
    {
        public string Key { get; set; }
        public string Id { get; set; }
        public bool Enabled { get; set; }

        /// <summary>The camera options as JSON, without the password.</summary>
        public string Options { get; set; }

        /// <summary>The password, encrypted (Data Protection); null without one.</summary>
        public string Password { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>A plugin folder row: <see cref="Key"/> is the lower-cased folder name.</summary>
    public sealed class PluginRecord
    {
        public string Key { get; set; }
        public string Folder { get; set; }
        public bool Enabled { get; set; }

        /// <summary>Assigned camera ids as a JSON array (empty: all cameras).</summary>
        public string Cameras { get; set; }

        /// <summary>The settings as JSON, encrypted (they may hold secrets); null without settings.</summary>
        public string Settings { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
