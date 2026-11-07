// Sarthak Killedar
// 2026-11-07
//
// Onvif.Open.Recorder: records every camera configured in appsettings.json ("Recording" section)
// and exposes a REST API for status, recordings and storage.

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Plugin;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Recorder.Api;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>().GetSection(RecorderOptions.SectionName).Get<RecorderOptions>() ?? new RecorderOptions());
builder.Services.AddSingleton(sp =>
    RecordingConfiguration.Create(sp.GetRequiredService<RecorderOptions>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<IRecordingManager>(sp => sp.GetRequiredService<ConfiguredRecording>().Manager);
builder.Services.AddSingleton(sp => new CameraEventStore(sp.GetRequiredService<IRecordingManager>()));
builder.Services.AddSingleton(sp => DiscoveryConfiguration.CreateDiscovery(
    sp.GetRequiredService<IConfiguration>().GetSection(DiscoveryOptions.SectionName).Get<DiscoveryOptions>()));
builder.Services.AddSingleton<IPluginHost>(sp => PluginConfiguration.CreateHost(
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
    sp.GetRequiredService<RecorderOptions>(),
    sp.GetRequiredService<ConfiguredRecording>(),
    sp.GetRequiredService<ILoggerFactory>()));

// Created eagerly so it sees events from the start.
builder.Services.AddHostedService(sp => new EagerSingleton<CameraEventStore>(sp));

// Order matters: plugins start before and stop after recording.
builder.Services.AddHostedService<PluginHostedService>();
builder.Services.AddHostedService<RecordingHostedService>();

var app = builder.Build();
app.MapRecordingApi();
app.MapPluginApi();
app.MapDiscoveryApi();
app.MapPtzApi();
app.MapImagingApi();
app.MapDeviceIoApi();
app.Run();

public partial class Program
{
}
