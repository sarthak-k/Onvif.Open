// Sarthak Killedar
// 2026-11-08

using BenchmarkDotNet.Running;

namespace Onvif.Open.Benchmarks
{
    /// <summary>
    /// The recording hot path, per camera and frame: RTSP read, RTP depacketizing, MPEG-TS muxing.
    /// dotnet run -c Release --project Onvif.Open/Onvif.Open.Benchmarks -- --filter '*'
    /// </summary>
    public static class Program
    {
        public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
