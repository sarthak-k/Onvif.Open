// Sarthak Killedar
// 2026-11-07
//
// Onvif.Open.Recorder: the all-in-one recorder. It is its organization's gateway (records the cameras kept in the
// settings database) and serves the REST API the web app uses. appsettings.json holds only how it starts and tuning.

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
using Onvif.Open.Recorder.Gateways;
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

// The gateway: this recorder, with its settings (storage limits, cloud backup, discovery) and heartbeat.
builder.Services.AddSingleton<IGatewayStore, EfGatewayStore>();
builder.Services.AddSingleton<GatewayService>();
builder.Services.AddSingleton<GatewayHeartbeat>();
builder.Services.AddSingleton<GatewayPairing>();

// Cases: clips of video with notes; the video of open cases is kept.
builder.Services.AddSingleton<Onvif.Open.Recorder.Cases.ICaseStore, Onvif.Open.Recorder.Cases.EfCaseStore>();
builder.Services.AddSingleton<Onvif.Open.Recorder.Cases.CaseHolds>();
builder.Services.AddSingleton<Onvif.Open.Recorder.Cases.CaseService>();

// MP4 exports with a watermark (ffmpeg, "Export:Ffmpeg"), kept in "Export:Directory" for 7 days; share links.
builder.Services.AddSingleton<Onvif.Open.Recorder.Exports.IExportStore, Onvif.Open.Recorder.Exports.EfExportStore>();
builder.Services.AddSingleton<Onvif.Open.Recorder.Exports.IMp4Encoder>(sp => new Onvif.Open.Recorder.Exports.FfmpegMp4Encoder(sp.GetRequiredService<IConfiguration>()["Export:Ffmpeg"], sp.GetRequiredService<IConfiguration>()["Export:FontFile"]));
builder.Services.AddSingleton(sp => new Onvif.Open.Recorder.Exports.ExportService(
    sp.GetRequiredService<Onvif.Open.Recorder.Exports.IExportStore>(),
    sp.GetRequiredService<IRecordingManager>(),
    sp.GetRequiredService<CameraDirectory>(),
    sp.GetRequiredService<ITenancyStore>(),
    sp.GetRequiredService<Onvif.Open.Recorder.Exports.IMp4Encoder>(),
    Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, sp.GetRequiredService<IConfiguration>()["Export:Directory"] ?? Path.Combine("data", "exports")),
    sp.GetRequiredService<ILogger<Onvif.Open.Recorder.Exports.ExportService>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<Onvif.Open.Recorder.Exports.ExportService>());

// Alerts: rules raise alerts into the inbox and notify by email ("Alerts:Smtp") and signed webhooks.
builder.Services.AddSingleton<Onvif.Open.Recorder.Alerts.IAlertStore, Onvif.Open.Recorder.Alerts.EfAlertStore>();
builder.Services.AddSingleton<Onvif.Open.Recorder.Alerts.AlertRuleCache>();
builder.Services.AddSingleton<Onvif.Open.Recorder.Alerts.IAlertNotifier>(sp => new Onvif.Open.Recorder.Alerts.EmailNotifier(
    sp.GetRequiredService<IConfiguration>().GetSection(Onvif.Open.Recorder.Alerts.SmtpOptions.SectionName).Get<Onvif.Open.Recorder.Alerts.SmtpOptions>()));
builder.Services.AddSingleton<Onvif.Open.Recorder.Alerts.IAlertNotifier>(_ => new Onvif.Open.Recorder.Alerts.WebhookNotifier(
    new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false })));
builder.Services.AddSingleton(sp => new Onvif.Open.Recorder.Alerts.AlertDelivery(
    sp.GetServices<Onvif.Open.Recorder.Alerts.IAlertNotifier>(),
    sp.GetRequiredService<ITenancyStore>(),
    sp.GetRequiredService<IConfiguration>()["Alerts:PublicUrl"],
    sp.GetRequiredService<ILogger<Onvif.Open.Recorder.Alerts.AlertDelivery>>()));
builder.Services.AddSingleton<Onvif.Open.Recorder.Alerts.AlertService>();
builder.Services.AddSingleton<Onvif.Open.Recorder.Alerts.AlertEngine>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Onvif.Open.Recorder.Alerts.AlertEngine>());

// With "ControlPlane:Url" this recorder is also a gateway of another one: it pairs with a code, then reports there.
var controlPlane = builder.Configuration["ControlPlane:Url"];
if (!string.IsNullOrWhiteSpace(controlPlane))
{
    builder.Services.AddSingleton(sp => new ControlPlaneClient(
        new System.Net.Http.HttpClient(new System.Net.Http.SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        {
            BaseAddress = new Uri(controlPlane.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        },
        new FileControlPlaneCredentials(
            Path.GetFullPath(Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, sp.GetRequiredService<IConfiguration>()["ControlPlane:CredentialsFile"] ?? Path.Combine("data", "control-plane.json"))),
            sp.GetRequiredService<IDataProtectionProvider>()),
        () => GatewayHeartbeat.Measure(sp.GetRequiredService<IRecordingManager>()),
        sp.GetRequiredService<IConfiguration>()["Gateway:Name"] is string name && name.Length > 0 ? name : Environment.MachineName,
        sp.GetRequiredService<IConfiguration>()["Gateway:Location"],
        sp.GetRequiredService<ILogger<ControlPlaneClient>>()));
    builder.Services.AddHostedService(sp => sp.GetRequiredService<ControlPlaneClient>());
}

// Read once at startup: the gateway, the cameras to record and the plugins to load. Later changes go through the API.
builder.Services.AddSingleton(sp => StartupSettings.LoadAsync(
    sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<RecorderDbContext>>(),
    sp.GetRequiredService<ISettingsStore>(),
    sp.GetRequiredService<ITenancyStore>(),
    sp.GetRequiredService<CameraDirectory>(),
    sp.GetRequiredService<GatewayService>(),
    sp.GetRequiredService<Onvif.Open.Recorder.Cases.CaseHolds>()).GetAwaiter().GetResult());
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>().GetSection(RecorderOptions.SectionName).Get<RecorderOptions>() ?? new RecorderOptions());
builder.Services.AddSingleton(sp =>
    RecordingConfiguration.Create(
        sp.GetRequiredService<RecorderOptions>(),
        sp.GetRequiredService<StartupSettings>().Cameras,
        sp.GetRequiredService<ILoggerFactory>(),
        StorageConfiguration.CreateArchive(
            sp.GetRequiredService<StartupSettings>().Gateway?.Backup,
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
            sp.GetRequiredService<ILoggerFactory>()),
        sp.GetRequiredService<PolicyRetention>().IsExpired,
        new GatewayRetention(() => sp.GetRequiredService<GatewayService>().Limits),
        sp.GetRequiredService<GatewayService>().Limits.MinFreeSpaceGB));
builder.Services.AddSingleton<IRecordingManager>(sp => sp.GetRequiredService<ConfiguredRecording>().Manager);
builder.Services.AddSingleton<CameraConfigurationService>();
builder.Services.AddSingleton<ILayoutStore, EfLayoutStore>();
builder.Services.AddSingleton<LayoutService>();
builder.Services.AddSingleton(sp => new LiveViewService(
    sp.GetRequiredService<IRecordingManager>(),
    sp.GetRequiredService<IConfiguration>().GetSection(LiveViewOptions.SectionName).Get<LiveViewOptions>(),
    sp.GetService<TimeProvider>(),
    sp.GetRequiredService<ConfiguredRecording>().FindSubStream,
    sp.GetRequiredService<ILogger<LiveViewService>>()));
builder.Services.AddSingleton(sp => new Onvif.Open.Recording.Implementation.Hls.RecordingPlaylist(sp.GetRequiredService<IRecordingManager>()));
builder.Services.AddSingleton<Onvif.Open.Core.Abstract.Interface.Discovery.ICameraDiscovery>(sp => new GatewayDiscovery(
    () => sp.GetRequiredService<GatewayService>().Discovery,
    sp.GetRequiredService<IConfiguration>().GetValue("Discovery:Timeout", TimeSpan.FromSeconds(3))));
builder.Services.AddSingleton<IPluginHost>(sp => PluginConfiguration.CreateHost(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
    sp.GetRequiredService<StartupSettings>().Plugins,
    sp.GetRequiredService<StartupSettings>().Cameras.Select(c => c.Id),
    sp.GetRequiredService<ConfiguredRecording>(),
    sp.GetRequiredService<ILoggerFactory>()));


// Stores what cameras, detections, recording and plugins report (subscribes before recording starts).
builder.Services.AddSingleton<EventRecorder>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EventRecorder>());

builder.Services.AddHostedService(sp => sp.GetRequiredService<GatewayHeartbeat>());

// Order matters: plugins start before and stop after recording.
builder.Services.AddHostedService<PluginHostedService>();
builder.Services.AddHostedService<RecordingHostedService>();

// "Schedule" policies: cameras record all the time during their hours.
builder.Services.AddSingleton<ScheduledRecording>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ScheduledRecording>());

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
app.MapGatewayApi();
app.MapCaseApi();
app.MapExportApi();
app.MapAlertApi();
app.Run();

public partial class Program
{
}
