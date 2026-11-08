// Sarthak Killedar
// 2026-11-08

using System;
using System.Linq;
using Onvif.Open.Core.Events;
using Onvif.Open.Recorder.Hosting;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class CameraEventStoreTest
    {
        [Fact]
        public void EventsShouldBeFilteredByTimeWindow()
        {
            var store = new CameraEventStore(null);
            var start = new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

            for (var minute = 0; minute < 5; minute++)
            {
                store.Add(new CameraEvent("lobby", "tns1:RuleEngine/CellMotionDetector/Motion", start.AddMinutes(minute), "Changed", null, null, null));
            }

            var window = store.Query("lobby", null, 100, start.AddMinutes(1), start.AddMinutes(3));

            // Newest first; from inclusive, to exclusive.
            Assert.Equal(new[] { start.AddMinutes(2), start.AddMinutes(1) }, window.Select(e => e.UtcTime));
            Assert.Equal(5, store.Query("lobby", null, 100).Count);
            Assert.Empty(store.Query("lobby", null, 100, start.AddMinutes(10)));
        }
    }
}
