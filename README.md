# Onvif.Open
Onvif Library for .NET

Targets **.NET 10 (LTS)** and runs on Windows, Linux and macOS.

## Projects

| Project | Purpose |
| --- | --- |
| `Onvif.Open.Core` | Abstractions (`ICamera`, `IMediaService`, `ILiveVideoStream`, ...), WS-Security UsernameToken authentication, shared SOAP binding |
| `Onvif.Open.Device.Management` | ONVIF Device service (device information, date & time, media service discovery) |
| `Onvif.Open.Media` | ONVIF Media service (profiles, stream URI, snapshot URI) |
| `Onvif.Open.Streaming` | Dependency-free RTSP/RTP client: Basic/Digest auth, RTP over TCP (interleaved) or UDP, H.264 and H.265 depacketizing |
| `Onvif.Open.Camera` | The `Camera` façade tying everything together |
| `Onvif.Open.Recording` | Multi-camera recording to files: segment rotation, retention, reconnects, MPEG-TS muxer |
| `Onvif.Open.Recorder` | Backend service: records the cameras from `appsettings.json`, hosts plugins and exposes a REST API |
| `Onvif.Open.Plugins` | Plugin runtime: isolated delivery of frames, camera state changes, segments and events; folder loader |
| `Onvif.Open.Plugins.Sample` | Template plugin project |
| `Onvif.Open.Sample` | Console app recording a camera's live stream to a file |

## Usage

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

A JPEG snapshot (ONVIF `GetSnapshotUri`, authenticated with the camera credentials):

```csharp
byte[] jpeg = await camera.InitializeMedia().GetSnapshotAsync();
```

Media profiles and URIs are available through the media service:

```csharp
var media = camera.InitializeMedia().GetMediaService();
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

### Recorder service

`Onvif.Open.Recorder` runs the recording manager as a service. Configure cameras and policies in
`Onvif.Open/Onvif.Open.Recorder/appsettings.json` (see `appsettings.Example.json`):

```json
"Recording": {
  "StorageDirectory": "/var/recordings",
  "Rotation": { "SegmentDuration": "00:10:00", "MaxSegmentSizeMB": 512 },
  "Retention": { "MaxAge": "7.00:00:00", "MaxSizePerCameraGB": 100, "MinFreeSpaceGB": 5 },
  "Cameras": [
    { "Id": "front-door", "OnvifUrl": "http://192.168.1.10", "Username": "admin", "Password": "..." },
    { "Id": "garage", "RtspUrl": "rtsp://192.168.1.11/stream1", "Username": "admin", "Password": "...",
      "Transport": "Udp", "Rotation": { "AlignToClock": "01:00:00" } }
  ]
}
```

```
dotnet run --project Onvif.Open/Onvif.Open.Recorder
```

Passwords can be supplied through environment variables instead of the file, e.g. `Recording__Cameras__0__Password`.

| Endpoint | |
| --- | --- |
| `GET /api/cameras`, `GET /api/cameras/{id}` | State, codec, frames/bytes recorded, current file, last error |
| `POST /api/cameras/{id}/start`, `POST /api/cameras/{id}/stop` | Control one camera |
| `GET /api/cameras/{id}/segments?from=&to=` | Recorded files with download URLs |
| `GET /api/cameras/{id}/segments/{file}` | Download (supports HTTP range requests) |
| `DELETE /api/cameras/{id}/segments/{file}` | Delete a completed file |
| `GET /api/storage` | Free space and usage per camera |
| `POST /api/retention` | Apply the retention policy now |
| `GET /api/cameras/{id}/snapshot` | JPEG snapshot (ONVIF cameras) |
| `GET /api/plugins` | Plugin state, delivered/dropped frames, errors |
| `GET /api/events?cameraId=&type=&source=&since=&severity=&limit=` | Plugin events, each linked to the recording covering it |
| `GET /health` | Liveness |

The API has no authentication and listens on `http://localhost:5080` by default. Put it behind a reverse proxy with
authentication before exposing it on a network.

## Plugins

Domain features such as exam proctoring or "camera went offline during opening hours" alerts are written as plugins
against a small SDK in `Onvif.Open.Core`. A plugin implements `IPlugin` plus any of `IFrameProcessor` (video frames),
`ICameraStateObserver` (online/offline), `ISegmentObserver` (completed files) and `IPluginEventObserver` (events from
other plugins). The host gives it settings, a logger, snapshots and recording control for its assigned cameras only,
and publishes its `PluginEvent`s through the API. Each plugin has its own queues, threads and load context, so a slow or
crashing plugin never affects recording or other plugins.

```json
"Plugins": {
  "Directory": "plugins",
  "Items": {
    "Contoso.ExamProctoring": { "Enabled": true, "Cameras": [ "room-101" ], "Settings": { "Threshold": "0.8" } }
  }
}
```

See [docs/PLUGINS.md](docs/PLUGINS.md) for the authoring guide and `Onvif.Open/Onvif.Open.Plugins.Sample` for a template.

## Build & test

```
dotnet build Onvif.Open.sln
dotnet test Onvif.Open.sln
```

The tests run against in-process fake ONVIF (SOAP) and RTSP servers and fake streams, so no camera is needed.

## Notes

* Transports: `StreamTransport.Rtsp` (default) receives RTP interleaved in the RTSP TCP connection and works through firewalls and NAT.
  `StreamTransport.Udp` receives RTP on a local even/odd UDP port pair, accepts packets from the device only, sends a small packet to the
  device's ports to open NAT/firewall pinholes and puts re-ordered packets back in sequence. If the device only offers TCP, starting a
  UDP stream fails with `NotSupportedException`; there is no automatic fallback to TCP. Multicast and RTSP-over-HTTP tunnelling are not supported.
* Codecs other than H.264/H.265 (e.g. MJPEG) are delivered as raw RTP payloads (`VideoCodec.Unknown`).
* Slow consumers do not block the network reader: once 256 frames are buffered (configurable with `SetFrameBufferCapacity`), the oldest are dropped.
