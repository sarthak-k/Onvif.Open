// Sarthak Killedar
// 2026-11-07
//
// Usage: dotnet run --project Onvif.Open/Onvif.Open.Sample -- <camera url> <username> <password> [output file] [seconds] [tcp|udp]
// Example: dotnet run --project Onvif.Open/Onvif.Open.Sample -- http://192.168.1.10 admin 12345 live.h264 30 udp
// Play the result with: ffplay live.h264   (or VLC)

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Camera.Implementation;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Sample
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: Onvif.Open.Sample <camera url> <username> <password> [output file] [seconds] [tcp|udp]");
                return 1;
            }

            var output = args.Length > 3 ? args[3] : "live.h264";
            var seconds = args.Length > 4 ? int.Parse(args[4]) : 10;
            var transport = args.Length > 5 && string.Equals(args[5], "udp", StringComparison.OrdinalIgnoreCase)
                ? StreamTransport.Udp
                : StreamTransport.Rtsp;

            var camera = CameraBuilder.GetBuilder()
                .SetUrl(args[0])
                .SetUsername(args[1])
                .SetPassword(args[2])
                .Build();

            var info = camera.Initialize().GetDeviceManagement().GetDeviceInfo();
            Console.WriteLine($"Device:  {info.Manufacturer} {info.Model} (firmware {info.FirmwareVersion})");

            var media = camera.InitializeMedia();
            foreach (var profile in media.GetMediaService().GetProfiles())
            {
                Console.WriteLine($"Profile: {profile}");
            }

            using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
            await using (var stream = await media.StartLiveStreamAsync(transport: transport))
            await using (var file = File.Create(output))
            {
                Console.WriteLine($"Recording {stream.Codec} over {stream.Transport} from {stream.StreamUri} to {output} for {seconds}s...");
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

                var frames = 0;
                try
                {
                    await foreach (var frame in stream.ReadFramesAsync(stop.Token))
                    {
                        await file.WriteAsync(frame.Data, stop.Token);
                        frames++;
                    }
                }
                catch (OperationCanceledException)
                {
                }

                Console.WriteLine($"Done: {frames} frames written.");
            }

            return 0;
        }
    }
}
