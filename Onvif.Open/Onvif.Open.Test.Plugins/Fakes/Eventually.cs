// Sarthak Killedar
// 2026-11-07

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;

namespace Onvif.Open.Test.Plugins.Fakes
{
    internal static class Eventually
    {
        public static async Task True(Func<bool> condition, string because, int timeoutMilliseconds = 10000)
        {
            var watch = Stopwatch.StartNew();

            while (!condition())
            {
                if (watch.ElapsedMilliseconds > timeoutMilliseconds)
                {
                    Assert.Fail($"Timed out waiting until {because}.");
                }

                await Task.Delay(10);
            }
        }
    }
}
