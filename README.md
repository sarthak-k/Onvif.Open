# Onvif.Open
Onvif Library for .NET

Targets **.NET 10 (LTS)** and runs on Windows, Linux and macOS.

## Projects

| Project | Purpose |
| --- | --- |
| `Onvif.Open.Core` | Abstractions (`ICamera`, `IMediaService`, `ILiveVideoStream`, ...), WS-Security UsernameToken authentication, shared SOAP binding |
| `Onvif.Open.Device.Management` | ONVIF Device service (device information, date & time, media service discovery) |
| `Onvif.Open.Media` | ONVIF Media service (profiles, stream URI, snapshot URI) |
| `Onvif.Open.Streaming` | Dependency-free RTSP/RTP client: Basic/Digest auth, RTP over RTSP (TCP interleaved), H.264 and H.265 depacketizing |
| `Onvif.Open.Camera` | The `Camera` façade tying everything together |
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
    .BuildAsync();
```

### Sample

```
dotnet run --project Onvif.Open/Onvif.Open.Sample -- http://192.168.1.10 admin 12345 live.h264 30
```

## Build & test

```
dotnet build Onvif.Open.sln
dotnet test Onvif.Open.sln
```

The tests run against in-process fake ONVIF (SOAP) and RTSP servers, so no camera is needed.

## Notes

* Video is received as RTP interleaved over the RTSP TCP connection, which works through firewalls and NAT. UDP transport is not implemented yet.
* Codecs other than H.264/H.265 (e.g. MJPEG) are delivered as raw RTP payloads (`VideoCodec.Unknown`).
* Slow consumers do not block the network reader: once 256 frames are buffered (configurable with `SetFrameBufferCapacity`), the oldest are dropped.
