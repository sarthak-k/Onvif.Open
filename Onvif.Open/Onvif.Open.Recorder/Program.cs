// Sarthak Killedar
// 2026-11-07
//
// Onvif.Open.Recorder: records every camera configured in appsettings.json ("Recording" section)
// and exposes a REST API for status, recordings and storage.

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Recorder.Api;
using Onvif.Open.Recorder.Audit;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Events;
using Onvif.Open.Recorder.Hosting;
using Onvif.Open.Recorder.Layouts;
using Onvif.Open.Recorder.Security;
using Onvif.Open.Recorder.Store;
using Onvif.Open.Recorder.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(TimeProvider.System);

// Users, roles and sign-in; CORS for the web application (Security:AllowedOrigins).
builder.Services.AddRecorderSecurity(builder.Configuration);

// The settings database (cameras, plugin assignments): SQLite by default, or SQL Server, PostgreSQL, MySQL.
builder.Services.AddSettingsDatabase(builder.Configuration, builder.Environment.ContentRootPath);

// Camera passwords and plugin settings are encrypted with keys kept on disk, apart from the database.
builder.Services.AddDataProtection()
    .SetApplicationName("Onvif.Open.Recorder")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(Path.Combine(
        builder.Environment.ContentRootPath, builder.Configuration["DataProtection:KeysDirectory"] ?? Path.Combine("data", "keys")))));

// Organizations, sites and recording policies; the cameras' owners and retention, kept in memory.
builder.Services.AddSingleton<ITenancyStore, EfTenancyStore>();
builder.Services.AddSingleton<CameraDirectory>();

// Events (stored, searchable) and the retention that keeps the video around them; the audit log.
builder.Services.AddSingleton<IEventStore, EfEventStore>();
builder.Services.AddSingleton<EventIndex>();
builder.Services.AddSingleton<PolicyRetention>();
builder.Services.AddSingleton<IAuditStore, EfAuditStore>();
builder.Services.AddSingleton(sp => new AuditLog(
    sp.GetRequiredService<IAuditStore>(),
    sp.GetRequiredService<ILogger<AuditLog>>(),
    sp.GetRequiredService<TimeProvider>(),
    TimeSpan.FromDays(sp.GetRequiredService<IConfiguration>().GetValue("Audit:RetentionDays", 365))));
builder.Services.AddHostedService(sp => sp.GetRequiredService<AuditLog>());
builder.Services.AddSingleton<TenancyService>();

// Read once at startup: the cameras to record and the plugins to load. Later changes go through the API.
builder.Services.AddSingleton(sp => StartupSettings.LoadAsync(
    sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<RecorderDbContext>>(),
    sp.GetRequiredService<ISettingsStore>(),
    sp.GetRequiredService<ITenancyStore>(),
    sp.GetRequiredService<CameraDirectory>()).GetAwaiter().GetResult());
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>().GetSection(RecorderOptions.SectionName).Get<RecorderOptions>() ?? new RecorderOptions());
builder.Services.AddSingleton(sp =>
    RecordingConfiguration.Create(
        sp.GetRequiredService<RecorderOptions>(),
        sp.GetRequiredService<StartupSettings>().Cameras,
        sp.GetRequiredService<ILoggerFactory>(),
        StorageConfiguration.CreateArchive(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
            sp.GetRequiredService<ILoggerFactory>()),
        sp.GetRequiredService<PolicyRetention>().IsExpired));
builder.Services.AddSingleton<IRecordingManager>(sp => sp.GetRequiredService<ConfiguredRecording>().Manager);
builder.Services.AddSingleton<CameraConfigurationService>();
builder.Services.AddSingleton<ILayoutStore, EfLayoutStore>();
builder.Services.AddSingleton<LayoutService>();
builder.Services.AddSingleton(sp => new LiveViewService(
    sp.GetRequiredService<IRecordingManager>(),
    sp.GetRequiredService<IConfiguration>().GetSection(LiveViewOptions.SectionName).Get<LiveViewOptions>()));
builder.Services.AddSingleton(sp => new Onvif.Open.Recording.Implementation.Hls.RecordingPlaylist(sp.GetRequiredService<IRecordingManager>()));
builder.Services.AddSingleton(sp => DiscoveryConfiguration.CreateDiscovery(
    sp.GetRequiredService<IConfiguration>().GetSection(DiscoveryOptions.SectionName).Get<DiscoveryOptions>()));
builder.Services.AddSingleton<IPluginHost>(sp => PluginConfiguration.CreateHost(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
    sp.GetRequiredService<StartupSettings>().Plugins,
    sp.GetRequiredService<StartupSettings>().Cameras.Select(c => c.Id),
    sp.GetRequiredService<ConfiguredRecording>(),
    sp.GetRequiredService<ILoggerFactory>()));


// Stores what cameras, detections, recording and plugins report (subscribes before recording starts).
builder.Services.AddHostedService<EventRecorder>();

// Order matters: plugins start before and stop after recording.
builder.Services.AddHostedService<PluginHostedService>();
builder.Services.AddHostedService<RecordingHostedService>();

var app = builder.Build();
app.UseRecorderSecurity();
app.UseCameraAccess();
app.MapSetupApi();
app.MapTenancyApi();
app.MapAuthApi();
app.MapRecordingApi();
app.MapCameraConfigurationApi();
app.MapPluginConfigurationApi();
app.MapPluginApi();
app.MapDiscoveryApi();
app.MapPtzApi();
app.MapImagingApi();
app.MapDeviceIoApi();
app.MapDeviceRecordingsApi();
app.MapPlaybackApi();
app.MapLayoutApi();
app.MapEventApi();
app.MapAuditApi();
app.Run();

public partial class Program
{
}
