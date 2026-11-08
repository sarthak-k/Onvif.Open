# Onvif.Open
Onvif Library for .NET

Targets **.NET 10 (LTS)** and runs on Windows, Linux and macOS.

## Projects

| Project | Purpose |
| --- | --- |
| `Onvif.Open.Core` | Abstractions (`ICamera`, `IMediaService`, `ILiveVideoStream`, ...), WS-Security UsernameToken authentication, shared SOAP binding |
| `Onvif.Open.Device.Management` | ONVIF Device service (device information, date & time, media service discovery) |
| `Onvif.Open.Media` | ONVIF Media (ver10) and Media2 (ver20) services: profiles, stream URI, snapshot URI |
| `Onvif.Open.Discovery` | WS-Discovery: find ONVIF devices on the network |
| `Onvif.Open.Events` | ONVIF Events service: PullPoint subscriptions (motion, tampering, inputs, ...) |
| `Onvif.Open.Ptz` | ONVIF PTZ service: pan/tilt/zoom moves, presets, home position |
| `Onvif.Open.Imaging` | ONVIF Imaging service: brightness, contrast, day/night (IR cut filter), WDR, focus |
| `Onvif.Open.Replay` | ONVIF Recording Search and Replay services (Profile G): recordings stored on the camera |
| `Onvif.Open.DeviceIo` | ONVIF DeviceIO service: digital inputs (relays use the Device service) |
| `Onvif.Open.Streaming` | Dependency-free RTSP/RTP client: Basic/Digest auth, RTP over TCP (interleaved) or UDP, H.264 and H.265 depacketizing |
| `Onvif.Open.Camera` | The `Camera` façade tying everything together |
| `Onvif.Open.Recording` | Multi-camera recording to files: segment rotation, retention, reconnects, MPEG-TS muxer |
| `Onvif.Open.Recorder` | Backend service: records the cameras from `appsettings.json`, hosts plugins and exposes a REST API |
| `Onvif.Open.Plugins` | Plugin runtime: isolated delivery of frames, camera state changes, segments and events; folder loader |
| `Onvif.Open.Plugins.Sample` | Template plugin project |
| `Onvif.Open.Storage.S3` / `.Azure` / `.Gcs` | Storage plugins: archive recordings to Amazon S3 (and S3-compatible), Azure Blob Storage, Google Cloud Storage |
| `Onvif.Open.Sample` | Console app recording a camera's live stream to a file |

## Usage

Find cameras on the network (WS-Discovery):

```csharp
foreach (var device in await CameraDiscoveryBuilder.GetBuilder().Build().FindAsync())
{
    Console.WriteLine($"{device.Name} ({device.Hardware}) at {device.DeviceServiceUri}");
}

var camera = CameraBuilder.GetBuilder()
    .SetDeviceServiceUrl(device.DeviceServiceUri.ToString())   // keeps the device's exact service path
    .SetUsername("admin").SetPassword("12345")
    .Build();
```

Probes go to the multicast group on every network interface. Cameras on other subnets (multicast is not routed)
can be probed directly with `.AddTarget(IPAddress.Parse("10.0.5.20"))`.

Connect to a known camera:

```csharp
var camera = CameraBuilder.GetBuilder()
    .SetUrl("http://192.168.1.10")
    .SetUsername("admin")
    .SetPassword("12345")
    .Build();

// Device management
var info = camera.Initialize().GetDeviceManagement().GetDeviceInfo();

// Live video (first media profile, or pass a profile token)
await using var stream = await camera.InitializeMedia().StartLiveStreamAsync();

await foreach (var frame in stream.ReadFramesAsync())
{
    // frame.Data: H.264/H.265 access unit in Annex-B format
    // frame.IsKeyFrame, frame.Timestamp, frame.Codec
}
```

Write the raw elementary stream to a file (playable with `ffplay live.h264` or VLC):

```csharp
await using var file = File.Create("live.h264");
await stream.CopyToAsync(file, cancellationToken);
```

RTP is received over the RTSP TCP connection by default. Use UDP for lower latency on networks where
the camera can reach you over UDP:

```csharp
await using var stream = await camera.InitializeMedia().StartLiveStreamAsync(transport: StreamTransport.Udp);
```

The camera's own events (motion, tampering, digital inputs, analytics rules), with the subscription kept alive
(renewed, re-created after errors, removed when you stop reading):

```csharp
await foreach (var e in camera.InitializeEvents().ReadEventsAsync(cancellationToken))
{
    if (e.MatchesTopic("RuleEngine/CellMotionDetector/Motion"))
        Console.WriteLine($"Motion: {e.GetState()}");      // TopicPath, Source, Data, UtcTime, ...
}
```

Pan/tilt/zoom (on the camera's first PTZ-enabled profile; speeds -1..1, positions pan/tilt -1..1 and zoom 0..1):

```csharp
var ptz = camera.InitializePtz();
await ptz.MoveAsync(pan: 0.5, tilt: 0, zoom: 0, duration: TimeSpan.FromSeconds(1));  // stops afterwards
await ptz.MoveAsync(pan: 0, tilt: 0, zoom: 1);                                        // zooms until...
await ptz.StopAsync();
await ptz.SavePresetAsync("Entrance");
await ptz.GoToPresetAsync("Entrance");                                                 // by name or token
await ptz.MoveToAsync(new PtzVector(pan: 0, tilt: 0, zoom: 0.5));
await ptz.GoHomeAsync();
var status = await ptz.GetStatusAsync();                                               // position, moving
```

A timed move always sends Stop at the end, even when cancelled or when the camera ignores the move timeout.
Cameras without PTZ throw `NotSupportedException`.

Image settings and focus (values in the device's own ranges, see `GetOptionsAsync`); updates change only the
properties you set, so vendor-specific settings are never overwritten:

```csharp
var imaging = camera.InitializeImaging();
var settings = await imaging.GetSettingsAsync();                    // Brightness, Contrast, IrCutFilter, WDR, ...
var options = await imaging.GetOptionsAsync();                      // e.g. options.Brightness.Min / Max
await imaging.UpdateAsync(new ImagingSettings
{
    IrCutFilter = IrCutFilterMode.Auto,                             // day/night switching
    Brightness = options.Brightness.FromFraction(0.6)               // 60 % of the device's range
});
await imaging.MoveFocusAsync(0.3, TimeSpan.FromMilliseconds(500)); // stops afterwards
```

Relay outputs (sirens, door openers, lights) and digital inputs (door contacts, buttons):

```csharp
var io = camera.InitializeIo();
var relays = await io.GetRelayOutputsAsync();                       // Token, Mode, IdleState, DelayTime
await io.PulseRelayAsync(relays[0].Token, TimeSpan.FromSeconds(3)); // on, then always off again
await io.SetRelayAsync(relays[0].Token, active: false);
var inputs = await io.GetDigitalInputsAsync();                      // needs the DeviceIO service
await foreach (var change in io.WatchDigitalInputsAsync())          // from the camera's events
{
    Console.WriteLine($"{change.Token}: {(change.IsActive ? "active" : "inactive")}");
}
```

Audio: request the audio track along with the video. Frames of both arrive in order, on one timeline:

```csharp
await using (var stream = await camera.InitializeMedia().StartLiveStreamAsync(null, StreamTransport.Rtsp, includeAudio: true))
{
    Console.WriteLine(stream.Audio);                                  // e.g. "Aac 16000 Hz, 1 channel(s)", null without audio
    await foreach (var frame in stream.ReadMediaFramesAsync())
    {
        if (frame is AudioFrame audio && audio.Codec == AudioCodec.Pcmu)
        {
            short[] pcm = G711.DecodeToPcm16(audio);                   // e.g. for audio analytics
        }
    }
}
```

AAC (RTP `mpeg4-generic`) is recorded into the MPEG-TS segments with the video. G.711 (PCMU/PCMA, the default of most
cameras) is delivered to the API and plugins but not recorded, because MPEG-TS has no widely playable G.711 mapping:
switch the camera's audio encoder to AAC to record sound. Audio needs `StreamTransport.Rtsp`.

What the camera's own analytics detect (ONVIF Profile M metadata): objects with class, confidence and position
(image fractions, 0..1 from the top left). Uses the first profile with a metadata configuration:

```csharp
await using (var analytics = await camera.InitializeMedia().StartAnalyticsStreamAsync())
{
    await foreach (var frame in analytics.ReadFramesAsync())
    {
        foreach (var detected in frame.Objects.Where(o => o.Is("Human", 0.6)))
        {
            Console.WriteLine($"{frame.UtcTime:O} person {detected.ObjectId} at {detected.BoundingBox}");
        }
    }
}
```

Recordings stored on the camera itself (SD card or NVR disk, ONVIF Profile G): find them, play them back, or export a clip.
Replay frames carry their recording time (`CaptureTime`), and the stream ends at `To` or at the end of the recording:

```csharp
var recordings = camera.InitializeRecordings();
var list = await recordings.GetRecordingsAsync();                   // Token, SourceName, Earliest, Latest, Tracks
var from = new DateTimeOffset(2026, 11, 5, 8, 15, 0, TimeSpan.Zero);

await using (var replay = await recordings.StartReplayAsync(list[0].Token, from, new ReplayOptions { Speed = 2 }))
{
    await foreach (var frame in replay.ReadFramesAsync())
    {
        Console.WriteLine($"{frame.CaptureTime:O} {frame.Data.Length} bytes");
    }
}

// Export a clip as fast as the camera can send it.
await using (var replay = await recordings.StartReplayAsync(list[0].Token, from, new ReplayOptions { To = from.AddMinutes(5), RealTime = false }))
await using (var file = File.Create("clip.ts"))
{
    await VideoExport.WriteAsync(replay, file);
}
```

A JPEG snapshot (ONVIF `GetSnapshotUri`, authenticated with the camera credentials):

```csharp
byte[] jpeg = await camera.InitializeMedia().GetSnapshotAsync();
```

Media profiles and URIs are available through the media service. Media2 (ver20, Profile T: describes H.265
encoders) is used when the camera offers it, otherwise Media1; force one with `CameraBuilder.SetMediaVersion(...)`
(or `"MediaVersion": "Media1"` per camera in the recorder) for devices with a faulty implementation:

```csharp
var media = camera.InitializeMedia().GetMediaService();      // media.Version: Media1 or Media2
var profiles = media.GetProfiles();
var rtspUri = media.GetStreamUri(profiles[0].Token);
var snapshotUri = media.GetSnapshotUri(profiles[0].Token);
```

Any RTSP URI can also be played directly:

```csharp
await using var stream = await LiveVideoStreamBuilder.GetBuilder()
    .SetStreamUri(new Uri("rtsp://192.168.1.10/Streaming/Channels/101"))
    .SetCredentials("admin", "12345")
    .SetTransport(StreamTransport.Udp)   // optional, default StreamTransport.Rtsp (TCP)
    .BuildAsync();
```

### Sample

```
dotnet run --project Onvif.Open/Onvif.Open.Sample -- http://192.168.1.10 admin 12345 live.h264 30 udp
```

## Recording

`Onvif.Open.Recording` records any number of cameras into segment files, reconnecting on its own
(exponential back-off, stall detection) and deleting old recordings according to your retention policy.

```csharp
await using var recorder = RecordingManagerBuilder.GetBuilder()
    .SetStorageDirectory("/var/recordings")
    .SetRotationPolicy(RotationPolicies.Every(TimeSpan.FromMinutes(10))
        .Or(RotationPolicies.MaxSize(512 * RotationPolicies.MiB)))
    .SetRetentionPolicy(RetentionPolicies.KeepFor(TimeSpan.FromDays(7))
        .And(RetentionPolicies.MinFreeSpace(10 * RotationPolicies.GiB)))
    .Build();

recorder.AddCamera("front-door", camera);                                        // ONVIF camera
recorder.AddCamera("garage", camera2, transport: StreamTransport.Udp,
    rotationPolicy: RotationPolicies.AlignedToClock(TimeSpan.FromHours(1)));     // per-camera override
recorder.AddCamera("legacy", ct => LiveVideoStreamBuilder.GetBuilder()           // any RTSP source
    .SetStreamUri(new Uri("rtsp://192.168.1.12/live")).SetCredentials("admin", "12345").BuildAsync(ct));

recorder.AddCamera("parking", camera3, new CameraRecordingOptions                  // record only around motion
{
    Mode = RecordingMode.Triggered,
    EventTriggers = { "RuleEngine/CellMotionDetector/Motion" },
    PreRoll = TimeSpan.FromSeconds(5),
    PostRoll = TimeSpan.FromSeconds(10)
});
recorder.TriggerRecording("parking", TimeSpan.FromSeconds(30));                  // or trigger it yourself

recorder.SegmentCompleted += (_, segment) => Console.WriteLine($"{segment.Path} ({segment.Size} bytes)");
await recorder.StartAsync();
```

* **Files**: `{storage}/{cameraId}/{yyyy-MM-dd}/{cameraId}_{yyyyMMddTHHmmssfffZ}.ts` (UTC). Recordings already on disk are
  picked up again after a restart.
* **Format**: MPEG-TS by default (keeps timestamps, plays in VLC/ffplay, stays readable if the process is killed);
  `SegmentFormats.ElementaryStream` writes raw `.h264`/`.h265`. Implement `ISegmentFormat` for others.
* **Rotation** (`IRotationPolicy`) is checked at every key frame, so each file starts with a key frame and plays on its own.
  Built-in: `Every(duration)`, `MaxSize(bytes)`, `AlignedToClock(interval)`, `When(predicate)`, combined with `Or`/`Any`.
* **Retention** (`IRetentionPolicy`) runs every minute and after each completed segment; the oldest files go first and the file being
  written is never deleted. Built-in: `KeepFor(age)`, `MaxTotalSize(bytes)`, `MaxSizePerCamera(bytes)`, `MinFreeSpace(bytes)`,
  `When(selector)`, `KeepAll`, combined with `And`/`All`. Default: keep at least 1 GiB free.
* **Storage**: `FileSystemRecordingStorage`, or implement `IRecordingStorage`.
* **Archive** (optional): `SetArchive(store)` uploads every completed segment to an `ISegmentStore`, i.e. the cloud
  or a NAS. The local disk stays the write buffer, so a slow or unreachable store never interrupts recording; uploads are
  retried and resumed after a restart, local copies are removed once uploaded (or kept for `KeepLocalCopyFor`), and the
  retention policy then applies to the archive. `GetSegments`, `OpenSegmentAsync` and `DeleteSegmentAsync` cover both
  places. See [docs/STORAGE.md](docs/STORAGE.md).
* **Triggered recording**: the stream stays connected and whole GOPs covering the pre-roll are buffered; while a trigger is
  active (stateful events such as motion hold it, `TriggerRecording`/plugins/stateless events pulse it) and for the
  post-roll afterwards, video is written. Rotation still applies within a triggered recording.
* **Events**: ONVIF cameras are subscribed to their events (`SubscribeToEvents`, on by default); every event is raised
  as `CameraEventReceived` and delivered to plugins implementing `ICameraEventObserver`.

```csharp
var manager = RecordingManagerBuilder.GetBuilder()
    .SetStorageDirectory("/var/recordings")                          // local buffer
    .SetArchive(new FileSystemSegmentStore("/mnt/nas/recordings"))   // or S3SegmentStore, AzureBlobSegmentStore, GcsSegmentStore
    .SetRetentionPolicy(RetentionPolicies.KeepFor(TimeSpan.FromDays(30)))
    .Build();
```

Clips and browser playback of recordings, in the library:

```csharp
await using (var file = File.Create("clip.ts"))
{
    await ClipExport.WriteAsync(manager, "front-door", from, to, file);   // starts at the key frame before 'from'
}

var playlist = await new RecordingPlaylist(manager).GetPlaylistAsync("front-door", from, to, s => $"segments/{s.FileName}");
var live = new HlsLiveStream();                                           // feed with FrameReceived / AudioReceived
```

### Recorder service

`Onvif.Open.Recorder` runs the recording manager as a service. Recording policies, storage and archive are configured
in `Onvif.Open/Onvif.Open.Recorder/appsettings.json` (see `appsettings.Example.json`).

Cameras and plugin assignments live in a settings database and are managed through the API while the recorder runs
(`/api/config/cameras`, `/api/config/plugins`). Bring the database you use (EF Core; tables `onvif_cameras` and
`onvif_plugins` are created on first start, also inside an existing database):

```json
"Database": { "Provider": "PostgreSql", "ConnectionString": "Host=db;Database=onvif;Username=onvif;Password=..." }
```

| Provider | Package | Notes |
| --- | --- | --- |
| `Sqlite` | Microsoft.EntityFrameworkCore.Sqlite | Default: `Data Source=data/onvif-open.db`, nothing to set up |
| `SqlServer` | Microsoft.EntityFrameworkCore.SqlServer | SQL Server 2016+, Azure SQL |
| `PostgreSql` | Npgsql.EntityFrameworkCore.PostgreSQL | |
| `MySql` | MySql.EntityFrameworkCore (Oracle) | MySQL 8+; MariaDB is not supported by this provider |

Camera passwords and plugin settings are encrypted by the recorder (ASP.NET Core Data Protection, keys in `data/keys`,
see `DataProtection:KeysDirectory`) before they reach the database: back up the keys with the database, and protect
both. Keep connection strings with passwords out of the file, e.g. `Database__ConnectionString` as an environment variable.

```json
"Recording": {
  "StorageDirectory": "/var/recordings",
  "Rotation": { "SegmentDuration": "00:10:00", "MaxSegmentSizeMB": 512 },
  "Retention": { "MaxAge": "7.00:00:00", "MaxSizePerCameraGB": 100, "MinFreeSpaceGB": 5 },
  "Archive": {
    "Provider": "Onvif.Open.Storage.S3",          // or "FileSystem" (NAS), "Onvif.Open.Storage.Azure", "Onvif.Open.Storage.Gcs"
    "Settings": { "Bucket": "my-recordings", "Region": "eu-west-1" }
  }
}
```

A camera, as sent to `POST /api/config/cameras`:

```json
{ "id": "entrance", "onvifUrl": "http://192.168.1.13", "username": "admin", "password": "...",
  "mode": "Triggered", "preRoll": "00:00:05", "postRoll": "00:00:10",
  "events": { "triggerTopics": [ "RuleEngine/CellMotionDetector/Motion" ] },
  "analytics": { "triggerClasses": [ "Human", "Vehicle" ], "minLikelihood": 0.6 },
  "audio": { "enabled": true } }
```

RTSP-only cameras use `rtspUrl` (optionally `"transport": "Udp"`). `analytics` uses the camera's own object detection
(ONVIF Profile M): `triggerClasses` records while such an object is in view (instead of on every motion event), and
`"enabled": true` passes detected objects to plugins.

```
dotnet run --project Onvif.Open/Onvif.Open.Recorder
```


| Endpoint | |
| --- | --- |
| `GET /api/cameras`, `GET /api/cameras/{id}` | State, codec, frames/bytes recorded, current file, last error |
| `POST /api/cameras/{id}/start`, `POST /api/cameras/{id}/stop` | Control one camera |
| `POST /api/cameras/{id}/trigger?seconds=30` | Triggered mode: record now (plus post-roll) |
| `GET /api/camera-events?cameraId=&topic=&limit=` | Recent ONVIF events from the cameras |
| `GET /api/cameras/{id}/segments?from=&to=` | Recorded files with download URLs |
| `GET /api/cameras/{id}/segments/{file}` | Download, from the local disk or the archive (supports HTTP range requests) |
| `DELETE /api/cameras/{id}/segments/{file}` | Delete a completed file (locally and from the archive) |
| `GET /api/storage` | Free space, usage per camera, archive state (pending uploads, last error) |
| `POST /api/retention` | Apply the retention policy now |
| `GET /api/cameras/{id}/snapshot` | JPEG snapshot (ONVIF cameras) |
| `GET /api/cameras/{id}/imaging`, `PATCH .../imaging` `{irCutFilter, brightness, ...}` | Image settings (PATCH changes only what is given) |
| `GET /api/cameras/{id}/imaging/options` | Supported ranges and modes |
| `GET/POST /api/cameras/{id}/imaging/focus` `{speed, durationMs}` or `{position}`, `POST .../focus/stop` | Focus |
| `GET /api/cameras/{id}/live.m3u8` | Live HLS (2 s segments, packaged only while watched) |
| `GET /api/cameras/{id}/recordings.m3u8?from=...&to=...` | HLS playback of the recordings (default: the last hour), split at key frames for a fast start |
| `GET /api/cameras/{id}/clip?from=...&to=...` | Download a clip cut from the recordings (MPEG-TS, across files, local or archived, up to 6 hours) |
| `GET /api/config/cameras`, `GET .../{id}` | Camera configuration (also disabled cameras); passwords are never returned (`hasPassword`) |
| `GET /api/config/plugins`, `PUT .../{folder}` `{enabled, cameras, settings}`, `DELETE .../{folder}` | Plugin assignments (applied at the next start); setting values are write-only |
| `POST /api/config/cameras`, `PUT .../{id}`, `DELETE .../{id}` | Add, change (password omitted: kept, `""`: removed) or remove a camera; applied immediately. Removing a camera assigned to a plugin is refused |
| `GET /api/cameras/{id}/device-recordings` | Recordings stored on the camera (Profile G) and their time span |
| `GET /api/cameras/{id}/device-recordings/{token}/export?from=...&to=...` | Download a clip from the camera's own storage as MPEG-TS (up to 6 hours) |
| `GET /api/cameras/{id}/io` | Relay outputs and digital inputs |
| `POST /api/cameras/{id}/io/relays/{token}` `{active}` or `{durationMs}`, `PUT .../relays/{token}/settings` `{mode, delayMs, idleState}` | Switch / pulse / configure a relay |
| `GET /api/cameras/{id}/ptz` | PTZ status (position, moving) |
| `POST /api/cameras/{id}/ptz/move` `{pan, tilt, zoom, durationMs}`, `POST .../ptz/stop` | Continuous move / stop |
| `POST /api/cameras/{id}/ptz/position` `{pan, tilt, zoom}` | Absolute move |
| `GET .../ptz/presets`, `PUT/DELETE .../ptz/presets/{name}`, `POST .../ptz/presets/{name}/goto` | Presets |
| `POST/PUT /api/cameras/{id}/ptz/home` | Go to / set home position |
| `GET /api/discovery` | ONVIF devices on the network (WS-Discovery), with `configuredCameraId` for those already set up |
| `GET /api/plugins` | Plugin state, delivered/dropped frames, errors |
| `GET /api/events?cameraId=&type=&source=&since=&severity=&limit=` | Plugin events, each linked to the recording covering it |
| `GET /health` | Liveness |

The API has no authentication and listens on `http://localhost:5080` by default. Put it behind a reverse proxy with
authentication before exposing it on a network.

## Plugins

Domain features such as exam proctoring or "camera went offline during opening hours" alerts are written as plugins
against a small SDK in `Onvif.Open.Core`. A plugin implements `IPlugin` plus any of `IFrameProcessor` (video frames),
`ICameraStateObserver` (online/offline), `ISegmentObserver` (completed files), `ICameraEventObserver` (the cameras'
own ONVIF events), `IAnalyticsObserver` (objects the cameras detect), `IAudioProcessor` (camera audio) and `IPluginEventObserver` (events from other plugins). Plugins can also trigger recording and
control PTZ (e.g. turn to a preset on an alarm), imaging (e.g. day/night on a schedule) and relays (e.g. sound a
siren on a detection), and read the camera's own recordings (e.g. footage from before an incident). The host gives it settings, a logger, snapshots and recording control for its assigned cameras only,
and publishes its `PluginEvent`s through the API. Each plugin has its own queues, threads and load context, so a slow or
crashing plugin never affects recording or other plugins.

Plugin folders live in `Plugins:Directory`; which ones run, for which cameras and with which settings is set through
the API (applied at the next start):

```
PUT /api/config/plugins/Contoso.ExamProctoring
{ "enabled": true, "cameras": [ "room-101" ], "settings": { "Threshold": "0.8" } }
```

See [docs/PLUGINS.md](docs/PLUGINS.md) for the authoring guide and `Onvif.Open/Onvif.Open.Plugins.Sample` for a template.

## Build & test

```
dotnet build Onvif.Open.sln
dotnet test Onvif.Open.sln
```

The tests run against in-process fake ONVIF (SOAP) and RTSP servers and fake streams, so no camera is needed.
Where ffmpeg is installed, recordings, live HLS, playlists and clips made from real H.264/AAC are decoded with it.
The cloud storage tests run against emulators when `ONVIF_OPEN_TEST_S3_URL`, `ONVIF_OPEN_TEST_AZURE_CONNECTION` and
`ONVIF_OPEN_TEST_GCS_URL` are set (CI starts moto, Azurite and fake-gcs-server) and are skipped otherwise.

## Notes

* Transports: `StreamTransport.Rtsp` (default) receives RTP interleaved in the RTSP TCP connection and works through firewalls and NAT.
  `StreamTransport.Udp` receives RTP on a local even/odd UDP port pair, accepts packets from the device only, sends a small packet to the
  device's ports to open NAT/firewall pinholes and puts re-ordered packets back in sequence. If the device only offers TCP, starting a
  UDP stream fails with `NotSupportedException`; there is no automatic fallback to TCP. Multicast and RTSP-over-HTTP tunnelling are not supported.
* Codecs other than H.264/H.265 (e.g. MJPEG) are delivered as raw RTP payloads (`VideoCodec.Unknown`).
* Slow consumers do not block the network reader: once 256 frames are buffered (configurable with `SetFrameBufferCapacity`), the oldest are dropped.
