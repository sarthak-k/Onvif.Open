// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Test.Recording.Fakes
{
    /// <summary>
    /// Wall clock controlled by the test (timers still run in real time).
    /// </summary>
    internal sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new object();
        private DateTimeOffset _now = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return _now;
            }
        }

        public void Advance(TimeSpan by)
        {
            lock (_sync)
            {
                _now += by;
            }
        }
    }
}
