// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Onvif.Open.Core.Events;
using Onvif.Open.Recording.Implementation.Recorder;
using Onvif.Open.Test.Recording.Fakes;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class TriggeredRecordingTest
    {
        #region Private Variables

        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

        #endregion

        #region Private Methods

        private static CameraEvent Motion(string state, string topic = "tns1:RuleEngine/CellMotionDetector/Motion", string source = "VSC_1")
        {
            return new CameraEvent(null, topic, T0, "Changed",
                new Dictionary<string, string> { ["VideoSourceConfigurationToken"] = source }, null,
                state == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["IsMotion"] = state });
        }

        #endregion

        #region Public Methods

        [Fact]
        public void TriggerShouldHoldWhileActiveAndForThePostRoll()
        {
            var trigger = new RecordingTrigger(TimeSpan.FromSeconds(10));

            Assert.False(trigger.IsActive(T0));

            trigger.Activate("motion", T0);
            Assert.True(trigger.IsActive(T0.AddHours(1)));

            trigger.Deactivate("motion", T0.AddMinutes(1));
            Assert.True(trigger.IsActive(T0.AddMinutes(1).AddSeconds(9)));
            Assert.False(trigger.IsActive(T0.AddMinutes(1).AddSeconds(11)));
            Assert.Equal(T0, trigger.LastTriggeredAt);
        }

        [Fact]
        public void TriggerShouldStayActiveWhileAnySourceIsActive()
        {
            var trigger = new RecordingTrigger(TimeSpan.Zero);

            trigger.Activate("zone-1", T0);
            trigger.Activate("zone-2", T0);
            trigger.Deactivate("zone-1", T0.AddSeconds(1));
            Assert.True(trigger.IsActive(T0.AddSeconds(2)));

            trigger.Deactivate("zone-2", T0.AddSeconds(3));
            Assert.False(trigger.IsActive(T0.AddSeconds(3)));

            // Deactivating an unknown source does not extend anything.
            trigger.Deactivate("never-active", T0.AddSeconds(4));
            Assert.False(trigger.IsActive(T0.AddSeconds(4)));
        }

        [Fact]
        public void PulseShouldLastItsDurationPlusPostRoll()
        {
            var trigger = new RecordingTrigger(TimeSpan.FromSeconds(5));

            trigger.Pulse(TimeSpan.FromSeconds(30), T0);
            trigger.Pulse(TimeSpan.FromSeconds(1), T0);

            Assert.True(trigger.IsActive(T0.AddSeconds(34)));
            Assert.False(trigger.IsActive(T0.AddSeconds(36)));
        }

        [Fact]
        public void PreRollShouldKeepWholeGopsCoveringThePreRoll()
        {
            var buffer = new PreRollBuffer(TimeSpan.FromMilliseconds(100));

            buffer.Add(FakeLiveVideoStream.Frame(1, false)); // before the first key frame: useless
            for (var i = 0; i < 15; i++)
            {
                buffer.Add(FakeLiveVideoStream.Frame(i, i % 5 == 0)); // 40 ms frames, GOP of 5 (200 ms)
            }

            var frames = buffer.Drain();

            // The newest GOP alone (frames 10-14, 160 ms) covers 100 ms of pre-roll.
            Assert.True(frames[0].IsKeyFrame);
            Assert.Equal(new[] { 10, 11, 12, 13, 14 }, frames.Select(f => (int)(f.Timestamp.TotalMilliseconds / 40)));
            Assert.Empty(buffer.Drain());
        }

        [Fact]
        public void PreRollShouldRespectItsMemoryLimit()
        {
            var buffer = new PreRollBuffer(TimeSpan.FromHours(1), maxBytes: 1000);

            for (var i = 0; i < 50; i++)
            {
                buffer.Add(FakeLiveVideoStream.Frame(i, i % 5 == 0, size: 100));
            }

            Assert.InRange(buffer.FrameCount, 1, 10);
            Assert.True(buffer.Drain()[0].IsKeyFrame);
        }

        [Fact]
        public void EventMonitorShouldDriveTheTriggerFromMatchingTopics()
        {
            var trigger = new RecordingTrigger(TimeSpan.FromSeconds(10));
            var forwarded = new List<CameraEvent>();
            var monitor = new CameraEventMonitor("cam1", null, new[] { "RuleEngine/CellMotionDetector" }, trigger, TimeProvider.System, NullLogger.Instance, forwarded.Add);

            monitor.Handle(Motion("true", topic: "tns1:VideoSource/ImageTooBlurry"));
            Assert.False(trigger.IsActive(DateTimeOffset.UtcNow));

            monitor.Handle(Motion("true"));
            Assert.True(trigger.IsActive(DateTimeOffset.UtcNow.AddHours(1)));

            monitor.Handle(Motion("false"));
            Assert.True(trigger.IsActive(DateTimeOffset.UtcNow.AddSeconds(5)));
            Assert.False(trigger.IsActive(DateTimeOffset.UtcNow.AddSeconds(15)));

            Assert.Equal(3, forwarded.Count);
            Assert.All(forwarded, e => Assert.Equal("cam1", e.CameraId));
        }

        [Fact]
        public void StatelessEventsShouldPulseTheTrigger()
        {
            var trigger = new RecordingTrigger(TimeSpan.FromSeconds(10));
            var monitor = new CameraEventMonitor("cam1", null, new[] { "RuleEngine/LineDetector/Crossed" }, trigger, TimeProvider.System, NullLogger.Instance, null);

            monitor.Handle(Motion(null, topic: "tns1:RuleEngine/LineDetector/Crossed"));

            Assert.True(trigger.IsActive(DateTimeOffset.UtcNow.AddSeconds(5)));
            Assert.False(trigger.IsActive(DateTimeOffset.UtcNow.AddSeconds(15)));
        }

        #endregion
    }
}
