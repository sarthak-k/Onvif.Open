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
using Microsoft.Extensions.Logging;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Recorder.Api;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recorder.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IConfiguration>().GetSection(RecorderOptions.SectionName).Get<RecorderOptions>() ?? new RecorderOptions());
builder.Services.AddSingleton<IRecordingManager>(sp =>
    RecordingConfiguration.CreateManager(sp.GetRequiredService<RecorderOptions>(), sp.GetRequiredService<ILoggerFactory>()));
builder.Services.AddHostedService<RecordingHostedService>();

var app = builder.Build();
app.MapRecordingApi();
app.Run();

public partial class Program
{
}
