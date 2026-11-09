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
| `Onvif.Open.Recorder` | Backend service (all-in-one: the gateway that records and the API the web app uses): records the cameras from its settings database, hosts plugins and exposes a REST API |
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

`Onvif.Open.Recorder` runs the recording manager as a service. `Onvif.Open/Onvif.Open.Recorder/appsettings.json` (see
`appsettings.Example.json`) only says how it starts (address, database, key and recording folders) and tunes the
engine (file rotation, reconnect delays, live view, upload concurrency); customers never need to edit it.

Everything a customer sets up lives in a settings database and is managed through the API while the recorder runs:
the **organization** (with its data region, Asia or US, chosen at setup), its **sites** (each with a time zone), its
**gateways**, its **recording policies**, **cameras** (each at a site, following a policy, recorded by a gateway),
**users** (a role, and optionally only some sites), saved live view layouts and plugin assignments.

**Gateways.** A gateway records the cameras of a site and keeps their video. The recorder is its organization's first
gateway ("this server", created at setup, named after the machine or `Gateway:Name`, `Gateway:Location` `Local` or
`Cloud`); later versions pair more gateways. It reports its health every 30 s (cameras recording and failing, disk
space, backup state, version) and shows as offline after two minutes without a report. Its settings:

* **Storage limits**: the free space to keep (1 GB by default), the space per camera and in total; the oldest
  recordings go first, on top of each camera's policy. Applied at the next retention run.
* **Cloud backup**: completed recordings are copied to a folder or NAS share, Amazon S3, Azure Blob Storage or Google
  Cloud Storage (see [docs/STORAGE.md](docs/STORAGE.md)). The settings (keys) are stored encrypted and never returned.
  Applied when the recorder restarts (`restartRequired` says when one is due).
* **Finding cameras**: multicast on the local network, plus addresses asked directly (other subnets). Applied at once.

**Pairing a gateway on another machine.** Install the recorder on a computer at the site and start it with
`ControlPlane:Url` set to this recorder's address (e.g. `ControlPlane__Url=https://cameras.example.com`, and optionally
`Gateway__Name`). It asks for a 6-digit code and writes it to its log ("Pair this gateway: ... enter the code 123 456");
an administrator enters it in the web app (Settings › Gateways › Add gateway) within 10 minutes. The gateway then
receives its own token (stored encrypted in `data/control-plane.json`, only a hash of it here) and reports its health
every 30 s, so it shows as online with its cameras, disk and backup state. It only makes outbound HTTPS requests:
nothing to open on the site's firewall. Removing it in the web app revokes its token; it then asks for a new code.
Paired gateways report their health; recording their cameras from here, and their live video, come with the tunnel
in a later version.

Camera ids are UUIDs assigned by the recorder, so recordings
never depend on a name someone typed; names can change at any time. Bring the database you use (EF Core; the `oo_*`
tables are created on first start, also inside an existing database, and tables added by later versions are created
in a database from an earlier one; until the first release, columns added to existing tables are not, so recreate a
database from an earlier build, e.g. delete `data/onvif-open.db`):

```json
"Database": { "Provider": "PostgreSql", "ConnectionString": "Host=db;Database=onvif;Username=onvif;Password=..." }
```

| Provider | Package | Notes |
| --- | --- | --- |
| `Sqlite` | Microsoft.EntityFrameworkCore.Sqlite | Default: `Data Source=data/onvif-open.db`, nothing to set up |
| `SqlServer` | Microsoft.EntityFrameworkCore.SqlServer | SQL Server 2016+, Azure SQL |
| `PostgreSql` | Npgsql.EntityFrameworkCore.PostgreSQL | |
| `MySql` | MySql.EntityFrameworkCore (Oracle) | MySQL 8+; MariaDB is not supported by this provider |

Camera passwords, plugin settings and backup settings are encrypted by the recorder (ASP.NET Core Data Protection, keys in `data/keys`,
see `DataProtection:KeysDirectory`) before they reach the database: back up the keys with the database, and protect
both. Keep connection strings with passwords out of the file, e.g. `Database__ConnectionString` as an environment variable.

#### Users and sign-in

**First run.** A recorder without users opens the web application's setup page: organization name and data region,
the first site and its time zone, and the owner's account (`GET/POST /api/setup`, open only until the first owner
exists; with `Security:SetupToken` configured it also asks for that token). Automated installs set
`Security:InitialAdmin:Password` instead (and optionally `UserName`, `OrganizationName`, `Region`, `SiteName`,
`TimeZone`): the organization and owner are then created at startup. Setup creates two recording policies: *Always
record* (the default: video kept 7 days, video around events 30 days) and *Events only* (video kept 30 days).

Every endpoint needs a signed-in user, except `/health`, sign-in and setup. Users have one of four roles in their
organization, each including the ones before it, and see either all sites or only the ones they were given:

| Role | Can |
| --- | --- |
| `Viewer` | Live view, recordings, clips, snapshots, camera status and events |
| `Operator` | Also start/stop/trigger recording, PTZ (also presets), focus, relays |
| `Admin` | Also cameras, plugins, imaging settings, relay settings, deleting recordings, retention, discovery; with all sites also sites, recording policies and users |
| `Owner` | Also owners (only owners make or change owners) and the organization's name; one enabled owner with all sites always remains |

A camera of a site the user may not see does not exist for them: every `/api/cameras/{id}/...` route answers 404, and
lists (cameras, events, layouts) leave it out.

```
POST /api/auth/login    { "userName": "admin", "password": "..." }
→ { "tokenType": "Bearer", "accessToken": "...", "expiresIn": 900, "refreshToken": "..." }
GET  /api/cameras        Authorization: Bearer <accessToken>
POST /api/auth/refresh  { "refreshToken": "..." }   → new tokens
```

* Users created by an administrator choose their own password first; until then only `/api/auth/me` and
  `/api/auth/password` work.
* Changing a user's password, role, sites or state, or the user's own password change, ends its sessions (within 30 s).
* After 5 wrong passwords an account is locked for 5 minutes; sign-ins are limited to 10 per minute and address.
* Video players and downloads that cannot send a header may pass the token as `?access_token=` on video requests only
  (playlists, segments, clips, snapshots, exports); playlists then carry it to their segments. Prefer the header
  (hls.js: `xhrSetup`).
* A web application on another origin must be listed in `Security:AllowedOrigins` (CORS).
* Serve the recorder over HTTPS (e.g. behind a reverse proxy): tokens and passwords travel in every request.

```json
"Recording": {
  "StorageDirectory": "/var/recordings",
  "Rotation": { "SegmentDuration": "00:10:00", "MaxSegmentSizeMB": 512 },
  "Archive": { "MaxConcurrentUploads": 2 }    // how backups upload; where to, and the storage limits, are gateway settings
},
"Gateway": { "Name": "Back office", "Location": "Local" }   // the name and place of the recorder's own gateway when it is created
```

The gateway's settings, as sent to `PUT /api/gateways/{id}` (each part optional):

```json
{ "name": "Back office",
  "storage": { "minFreeSpaceGB": 5, "maxSizePerCameraGB": 100, "maxTotalSizeGB": null },
  "backup": { "provider": "Onvif.Open.Storage.S3", "prefix": "back-office", "keepLocalCopyHours": 24,
              "settings": { "Bucket": "my-recordings", "Region": "eu-west-1" } },
  "discovery": { "multicast": true, "addresses": [ "10.0.5.20" ] } }
```

`"settings": null` keeps the stored backup settings; `"provider": ""` turns the backup off.

A camera, as sent to `POST /api/config/cameras`:

```json
{ "name": "Entrance", "siteId": "<a site id>", "policyId": "<a policy id; omitted: the default>",
  "onvifUrl": "http://192.168.1.13", "username": "admin", "password": "...",
  "events": { "triggerTopics": [ "RuleEngine/CellMotionDetector/Motion" ] },
  "analytics": { "triggerClasses": [ "Human", "Vehicle" ], "minLikelihood": 0.6 },
  "audio": { "enabled": true } }
```

The recorder answers with the camera's `id`. Its policy sets how it records (always, or only around events with pre-
and post-roll) and how long its video is kept; `triggerTopics` and `triggerClasses` choose the events that start a
recording. RTSP-only cameras use `rtspUrl` (optionally `"transport": "Udp"`). `analytics` uses the camera's own object detection
(ONVIF Profile M): `triggerClasses` records while such an object is in view (instead of on every motion event), and
`"enabled": true` passes detected objects to plugins.

**Recording policies.** Setup creates three: *Always record* (the default), *Events only* (around events, with 5 s
before and 10 s after) and *Business hours* (`Schedule`: all the time Monday to Friday 08:00 to 18:00, around events
otherwise). A schedule's hours are weekly time ranges in each camera's site time zone; a range whose end is not after
its start runs past midnight (22:00 to 06:00). The recorder checks the schedules every 30 s, so recording starts within
30 s of the hours and stops at their end (plus the post-roll).

**Events and how long video is kept.** Everything a camera reports is stored as an event (it survives restarts and
can be searched with `/api/events`): stateful camera events from their start to their end, detections of the same kind
merged while they keep being seen (ending 5 s after the last sighting), the time a camera was offline, and what plugins
report. A camera's policy keeps all its video for `continuousRetentionDays` (7 by default) and video around events
(30 s before and after) for `eventRetentionDays` (30 by default); cameras that record only around events keep their
video for `eventRetentionDays`. Events are kept as long as their camera's `eventRetentionDays`; those about no camera or a removed one for a year.

**Audit log.** Changes (cameras, sites, policies, users, layouts, plugins, the organization), sign-ins, password
changes, starting and stopping recording, relay switching, and who watched live video, played recordings or exported
video (a view is recorded once per user and camera every 10 minutes) go to the audit log, kept for
`Audit:RetentionDays` (365 by default). Refused requests are not recorded, failed sign-ins are.

```
dotnet run --project Onvif.Open/Onvif.Open.Recorder
```


| Endpoint | |
| --- | --- |
| `GET /api/cameras`, `GET /api/cameras/{id}` | State, codec, frames/bytes recorded, current file, last error |
| `POST /api/cameras/{id}/start`, `POST /api/cameras/{id}/stop` | Control one camera |
| `POST /api/cameras/{id}/trigger?seconds=30` | Triggered mode: record now (plus post-roll) |
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
| `POST /api/auth/login`, `POST /api/auth/refresh` | Sign in (access + refresh token), renew the access token |
| `GET /api/auth/me`, `POST /api/auth/password` `{currentPassword, newPassword}` | The own account; a password change returns new tokens |
| `GET /api/setup`, `POST /api/setup` `{organizationName, region, siteName, timeZone, userName, password, setupToken?}` | First-run setup (anonymous, until the first owner exists); returns the owner's tokens |
| `GET /api/organization`, `PUT /api/organization` `{name}` | The organization (name, data region); owners rename it |
| `GET /api/sites`, `POST /api/sites` `{name, timeZone, address}`, `PUT .../{id}`, `DELETE .../{id}` | Sites the user sees; a site with cameras, and the last site, cannot be deleted |
| `GET /api/recording-policies`, `POST` `{name, mode: Always/EventsOnly/Schedule, preRollSeconds, postRollSeconds, continuousRetentionDays, eventRetentionDays, isDefault, schedule: [{days: ["Monday", ...], start: "08:00", end: "18:00"}]}`, `PUT .../{id}`, `DELETE .../{id}` | Recording policies; a change applies to its cameras at once; the default and policies in use cannot be deleted |
| `GET /api/users`, `POST /api/users` `{userName, password, role, sites}`, `PUT .../{id}` `{role, sites, enabled, password}`, `DELETE .../{id}` | Members of the organization (administrators who see all sites); `sites: []` = all sites |
| `GET /api/config/plugins`, `PUT .../{folder}` `{enabled, cameras, settings}`, `DELETE .../{folder}` | Plugin assignments (applied at the next start); setting values are write-only |
| `POST /api/config/cameras/test?snapshot=true` | Try a camera before saving it: sign-in, device, media profiles and capabilities (ONVIF) or codec (RTSP). ONVIF cameras also get `recommended` streams (`mainProfile`: the sharpest, H.264 before H.265 at the same size; `subProfile`: the smallest other one) and, with `snapshot=true`, a picture as a data URL. With `id` and no password the stored password is used |
| `POST /api/config/cameras`, `PUT .../{id}`, `DELETE .../{id}` | Add (the recorder assigns the id), change (name, site, policy, connection; password omitted: kept, `""`: removed) or remove a camera; applied immediately. Removing a camera assigned to a plugin is refused |
| `GET /api/cameras/{id}/device-recordings` | Recordings stored on the camera (Profile G) and their time span |
| `GET /api/cameras/{id}/device-recordings/{token}/export?from=...&to=...` | Download a clip from the camera's own storage as MPEG-TS (up to 6 hours) |
| `GET /api/cameras/{id}/io` | Relay outputs and digital inputs |
| `POST /api/cameras/{id}/io/relays/{token}` `{active}` or `{durationMs}`, `PUT .../relays/{token}/settings` `{mode, delayMs, idleState}` | Switch / pulse / configure a relay |
| `GET /api/cameras/{id}/ptz` | PTZ status (position, moving) |
| `POST /api/cameras/{id}/ptz/move` `{pan, tilt, zoom, durationMs}`, `POST .../ptz/stop` | Continuous move / stop |
| `POST /api/cameras/{id}/ptz/position` `{pan, tilt, zoom}` | Absolute move |
| `GET .../ptz/presets`, `PUT/DELETE .../ptz/presets/{name}`, `POST .../ptz/presets/{name}/goto` | Presets |
| `POST/PUT /api/cameras/{id}/ptz/home` | Go to / set home position |
| `GET /api/discovery` | ONVIF devices on the network (WS-Discovery, with the gateway's discovery settings), with `configuredCameraId` for those already set up |
| `GET /api/gateways`, `GET .../{id}` | Gateways with `status` (Online, Offline, Waiting), version, last report, `health` and `restartRequired`; the settings only for administrators who see all sites |
| `PUT /api/gateways/{id}` `{name, storage, backup, discovery}` | Change a gateway's settings (administrators who see all sites); backup setting values are write-only |
| `POST /api/gateways/pair` `{code, name}`, `DELETE /api/gateways/{id}` | Pair the gateway that shows this code, remove a paired gateway (administrators who see all sites; this server's own gateway and gateways with cameras cannot be removed) |
| `POST /api/gateways/pairing` `{name, location}`, `GET .../pairing/{id}` (header `X-Pairing-Secret`), `POST /api/gateways/heartbeat` (header `X-Gateway-Token`) | Used by gateways: ask for a code, collect the token once the code was entered, report health |
| `GET /api/layouts`, `POST /api/layouts` `{name, grid, cameras, shared}`, `PUT .../{id}`, `DELETE .../{id}` | Live view layouts: each user's own, plus shared ones (managed by administrators); the grid is the web app's template id and `cameras` the camera of each tile |
| `GET /api/plugins` | Plugin state, delivered/dropped frames, errors |
| `GET /api/events?cameraId=&siteId=&category=&from=&to=&before=&limit=` | Stored events, newest first: what cameras report (motion, tamper, inputs), detections (person, vehicle, object), cameras going offline, and plugin findings; each with its start and end. `category` is one of motion, person, vehicle, object, intrusion, tamper, input, offline, plugin, other; `before` pages |
| `GET /api/audit?userId=&action=&from=&to=&before=&limit=` | The audit log (administrators who see all sites): changes, sign-ins (also failed ones), who watched live or recorded video and who exported it; `action` is an action (`camera.update`) or an area (`camera`) |
| `GET /health` | Liveness |

The API listens on `http://localhost:5080` by default. Put it behind a reverse proxy with HTTPS before exposing it on
a network: bearer tokens and passwords must not travel in clear text.

## Web app

`web/` is a React + TypeScript app for the recorder: first-run setup, sign-in, live view (several cameras in a grid: single, 2 × 2 up to
5 × 5, or one large tile with smaller ones; arrange cameras by picking or dragging, enlarge one with a double click,
full screen; layouts saved per user or shared by administrators), **Investigate** (search the stored events by site,
camera, kind and time, newest first; open one to play its camera from 5 s before it, and up to three more cameras of the
site in sync, with one timeline of what each recorded and the site's events: play, pause, speed up to 8×, ±10 s, click
the timeline or an event mark to move all cameras there), cameras (status for everyone; for administrators an
**Add cameras** wizard: it lists what the gateway found on the network (or a typed address), signs in to all of them
with one shared password (a camera that refuses it gets its own), shows each camera's picture, chooses its streams,
and adds them named, at a site and with a recording policy; one camera can also be added and edited in full), settings (sites, recording policies, gateways with their
health, storage limits, cloud backup and discovery, users with roles and site access, the audit log, the organization) and the own account. It is a
static site served separately from the recorder:

```
cd web
npm ci
npm run dev            # http://localhost:5173, proxies /api to the recorder on http://localhost:5080
npm run build          # dist/: host it on any web server
```

A deployed app reads `config.json` next to `index.html` for the recorder's URL, so one build fits any recorder:

```json
{ "apiBaseUrl": "https://recorder.example.com" }
```

and the recorder must allow the app's origin: `"Security": { "AllowedOrigins": [ "https://cameras.example.com" ] }`.
The access token stays in memory and is renewed before it expires; the refresh token is kept for the browser tab, or
in the browser with "Keep me signed in".

The app is layered so each part has one job and depends only on the layer below:

| Folder | Role |
| --- | --- |
| `src/api` | HTTP transport, session (token renewal), and the recorder's API as interfaces (`RecorderApi`) |
| `src/data` | TanStack Query hooks per area (`useCameraStatus`, `useSaveCamera`, ...) and the query-key factory |
| `src/features` | Pages and their parts per feature (`auth`, `cameras`, `users`); permissions in `auth/permissions.ts` |
| `src/shared` | Form fields, `useForm`, dialogs, error display |
| `src/app` | Composition root, route table (lazy-loaded pages, navigation and access derived from it), layout |

A new page is one entry in `app/routes.tsx`; tests and other back ends can pass their own `RecorderApi` to `App`.

`npm test` runs the component tests, `npm run e2e` the end-to-end tests (Playwright) against a real recorder started
with an empty database.

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

## Performance

Every camera runs the same path many times a second: RTSP read → RTP depacketizing → MPEG-TS muxing (→ plugins, live
view). `Onvif.Open.Benchmarks` measures it on one second of synthetic 1080p25 H.264 (about 510 KB):

```
dotnet run -c Release --project Onvif.Open/Onvif.Open.Benchmarks -- --filter '*'
```

| Stage | Time | Allocated |
| --- | --- | --- |
| RTSP: read interleaved RTP | 129 µs | 592 KB (the packets themselves) |
| RTP: depacketize H.264 | 202 µs | 950 KB (the frames, plus the reused buffer's first growth) |
| MPEG-TS: mux video + audio | 27 µs | 12 KB |

Access units are assembled in place in one reused buffer and copied once into an uninitialized array; the muxer writes
PES headers from the stack straight into batched TS packets; plugin delivery completes synchronously finished
`ValueTask`s without a `Task`; locks use `System.Threading.Lock`; regular expressions are source generated.

## Notes

* Transports: `StreamTransport.Rtsp` (default) receives RTP interleaved in the RTSP TCP connection and works through firewalls and NAT.
  `StreamTransport.Udp` receives RTP on a local even/odd UDP port pair, accepts packets from the device only, sends a small packet to the
  device's ports to open NAT/firewall pinholes and puts re-ordered packets back in sequence. If the device only offers TCP, starting a
  UDP stream fails with `NotSupportedException`; there is no automatic fallback to TCP. Multicast and RTSP-over-HTTP tunnelling are not supported.
* Codecs other than H.264/H.265 (e.g. MJPEG) are delivered as raw RTP payloads (`VideoCodec.Unknown`).
* Slow consumers do not block the network reader: once 256 frames are buffered (configurable with `SetFrameBufferCapacity`), the oldest are dropped.
