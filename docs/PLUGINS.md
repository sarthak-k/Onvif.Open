# Writing plugins

Plugins add behaviour to the recorder without changing it: video analytics, schedule-based alerts,
notifications, uploads, ... Onvif.Open only provides the extension points; the plugins are yours.

## 1. Create the project

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Contoso.ExamProctoring</AssemblyName>
    <EnableDynamicLoading>true</EnableDynamicLoading>
  </PropertyGroup>
  <ItemGroup>
    <!-- The contracts come from the host: compile against them, don't ship them. -->
    <ProjectReference Include="path/to/Onvif.Open.Core.csproj">
      <Private>false</Private>
      <ExcludeAssets>runtime</ExcludeAssets>
    </ProjectReference>
    <!-- Your own dependencies (ML runtime, OpenCV, HTTP clients, ...) are shipped with the plugin. -->
  </ItemGroup>
</Project>
```

`Onvif.Open/Onvif.Open.Plugins.Sample` is a complete, minimal template.

## 2. Implement the capabilities you need

Every plugin implements `IPlugin` (`Name`, `StartAsync`, `StopAsync`) and any of:

| Interface | You receive | Typical use |
| --- | --- | --- |
| `IFrameProcessor` | Every frame (or only key frames, `FrameFilter.KeyFramesOnly`) of your cameras, H.264/H.265 Annex-B | Video analytics |
| `ICameraStateObserver` | `CameraStateChange`: `Connecting`, `Recording`, `Reconnecting`, `Stopped`, with the error; `IsConnectionLost` / `IsConnectionEstablished` | Offline/tampering alerts |
| `ISegmentObserver` | Each completed recording file | Upload, indexing, transcoding |
| `ICameraEventObserver` | The cameras' own ONVIF events (`CameraEvent`: topic, state, source, data) | React to camera motion/tamper/input detections |
| `IPluginEventObserver` | Events published by *other* plugins | E-mail/SMS/webhook notifications |

`StartAsync` receives an `IPluginContext`:

* `Settings`: your configuration (`Plugins:Items:<folder>:Settings`), keys like `Schedule:Start`.
* `Logger`, `TimeProvider`.
* `Cameras`: status, segments, `StartRecordingAsync` / `StopRecordingAsync`, `TriggerRecording` (for cameras in
  triggered mode: record around what you detected), and `GetSnapshotAsync` (JPEG through ONVIF,
  handy when you don't want to decode H.264). Only the cameras assigned to your plugin are accessible.
* `PublishAsync(PluginEvent)`: report what you found. Events appear in `GET /api/events`, each linked to the recording
  segment (and offset) covering its timestamp, and are forwarded to `IPluginEventObserver` plugins.

### Guarantees

* Callbacks of one capability are never invoked concurrently; frames and notifications are delivered in order.
* Your plugin has its own queues and threads. If it is slow, its oldest queued frames are dropped (see `FramesDropped`
  in `GET /api/plugins`); recording and other plugins are never slowed down.
* Exceptions in callbacks are logged and counted, and delivery continues. An exception in `StartAsync` marks the plugin `Faulted`.
* On shutdown queued notifications are delivered (bounded by `StopTimeout`), then `StopAsync` and `Dispose[Async]` are called.

## 3. Deploy

Copy the build output into the recorder's plugin directory, in a folder named after the assembly, and enable it:

```
plugins/
  Contoso.ExamProctoring/
    Contoso.ExamProctoring.dll
    Contoso.ExamProctoring.deps.json
    ...dependencies
```

```json
"Plugins": {
  "Directory": "plugins",
  "Items": {
    "Contoso.ExamProctoring": {
      "Enabled": true,
      "Cameras": [ "room-101", "room-102" ],
      "Settings": { "Threshold": "0.8", "Schedule:Start": "09:00", "Schedule:End": "12:00" }
    }
  }
}
```

Only folders listed and enabled under `Items` are loaded. Each plugin folder gets its own `AssemblyLoadContext`:
your dependencies may use other versions than the host's, except for the assemblies the host already provides
(Onvif.Open.Core, Microsoft.Extensions.*), which are shared.

Without the recorder service, host plugins in your own application:

```csharp
await using var plugins = PluginHostBuilder.GetBuilder()
    .SetRecordingManager(recorder)
    .AddPlugin(new MyPlugin(), cameras: new[] { "room-101" })
    .AddPlugins(PluginLoader.LoadFromFolder("plugins/Contoso.ExamProctoring"))
    .Build();
await plugins.StartAsync();
```

## Sketches

These show how typical plugins map onto the extension points; they are not shipped.

**Exam proctoring (analytics):**

```csharp
public sealed class ExamProctoringPlugin : IFrameProcessor
{
    private IPluginContext _context;
    public string Name => "ExamProctoring";
    public FrameFilter FrameFilter => FrameFilter.KeyFramesOnly;      // ~1 picture per second is plenty

    public Task StartAsync(IPluginContext context, CancellationToken ct) { _context = context; /* load model */ return Task.CompletedTask; }

    public async ValueTask ProcessFrameAsync(CameraFrame frame, CancellationToken ct)
    {
        var image = Decode(frame.Frame.Data);                          // your decoder, or _context.Cameras.GetSnapshotAsync(...)
        var score = RunModel(image);
        if (score > Threshold)
        {
            _context.Cameras.TriggerRecording(frame.CameraId, TimeSpan.FromSeconds(30));   // keep the evidence
            await _context.PublishAsync(new PluginEvent("exam.suspicious-behaviour", frame.CameraId,
                "Possible cheating", PluginEventSeverity.Warning,
                new Dictionary<string, string> { ["score"] = score.ToString("0.00") }, frame.ReceivedAt), ct);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

**Camera shut off during hours of interest (alert):**

```csharp
public sealed class ScheduledOutagePlugin : ICameraStateObserver
{
    private IPluginContext _context;
    public string Name => "ScheduledOutage";

    public Task StartAsync(IPluginContext context, CancellationToken ct) { _context = context; /* read Schedule:* settings */ return Task.CompletedTask; }

    public async Task OnCameraStateChangedAsync(CameraStateChange change, CancellationToken ct)
    {
        if (change.IsConnectionLost && IsWithinHoursOfInterest(change.Timestamp))
            await _context.PublishAsync(new PluginEvent("camera.offline-during-schedule", change.CameraId,
                change.Error, PluginEventSeverity.Critical, timestamp: change.Timestamp), ct);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
```

A camera that never comes online at startup goes `Connecting → Reconnecting` without passing through `Recording`;
handle that transition too if it matters to you.
