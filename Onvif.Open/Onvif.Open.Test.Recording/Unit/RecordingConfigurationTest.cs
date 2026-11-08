// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Api;
using Onvif.Open.Recorder.Configuration;
using Onvif.Open.Recording.Implementation.Policy;
using Xunit;

namespace Onvif.Open.Test.Recording.Unit
{
    public class RecordingConfigurationTest
    {
        #region Private Methods

        private sealed class Progress : ISegmentProgress
        {
            public string CameraId { get; set; } = "cam";
            public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UnixEpoch;
            public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
            public TimeSpan Duration { get; set; }
            public long Size { get; set; }
            public int FrameCount { get; set; }
        }

        private static List<CameraOptions> Options(params CameraOptions[] cameras)
        {
            return new List<CameraOptions>(cameras);
        }

        #endregion

        #region Public Methods

        [Fact]
        public void EmptyRotationShouldFallBackToDefault()
        {
            Assert.Null(RecordingConfiguration.ToRotationPolicy(new RotationOptions()));
            Assert.Null(RecordingConfiguration.ToRotationPolicy(null));
        }

        [Fact]
        public void RotationShouldCombineDurationAndSize()
        {
            var policy = RecordingConfiguration.ToRotationPolicy(new RotationOptions { SegmentDuration = TimeSpan.FromMinutes(5), MaxSegmentSizeMB = 1 });

            Assert.False(policy.ShouldRotate(new Progress { Duration = TimeSpan.FromMinutes(1), Size = 10 }));
            Assert.True(policy.ShouldRotate(new Progress { Duration = TimeSpan.FromMinutes(5) }));
            Assert.True(policy.ShouldRotate(new Progress { Size = RotationPolicies.MiB }));
        }

        [Fact]
        public void RetentionShouldMapEveryRule()
        {
            var now = new DateTimeOffset(2026, 11, 7, 0, 0, 0, TimeSpan.Zero);
            var old = new RecordingSegment("cam", "/old.ts", now.AddDays(-10), now.AddDays(-10), 10, false);
            var recent = new RecordingSegment("cam", "/new.ts", now.AddHours(-1), now.AddHours(-1), 10, false);
            var policy = RecordingConfiguration.ToRetentionPolicy(new RetentionOptions { MaxAge = TimeSpan.FromDays(7), MinFreeSpaceGB = null });

            Assert.Equal(new[] { old }, policy.SelectForDeletion(new RetentionContext(new[] { old, recent }, now, null)));
        }

        [Fact]
        public void RetentionWithoutRulesShouldKeepEverything()
        {
            Assert.Same(RetentionPolicies.KeepAll, RecordingConfiguration.ToRetentionPolicy(new RetentionOptions { MinFreeSpaceGB = null }));
        }

        [Theory]
        [InlineData("../cam", "http://192.168.1.3", null, "invalid")]
        [InlineData("cam1", "http://192.168.1.3", "rtsp://192.168.1.3/s", "exactly one")]
        [InlineData("cam1", null, null, "exactly one")]
        public void InvalidCamerasShouldBeReported(string id, string onvif, string rtsp, string message)
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                RecordingConfiguration.ValidateCameras(Options(new CameraOptions { Id = id, OnvifUrl = onvif, RtspUrl = rtsp })));

            Assert.Contains(message, error.Message);
        }

        [Fact]
        public void DuplicateCameraIdsShouldBeReported()
        {
            var error = Assert.Throws<InvalidOperationException>(() => RecordingConfiguration.ValidateCameras(Options(
                new CameraOptions { Id = "cam1", RtspUrl = "rtsp://192.168.1.3/a" },
                new CameraOptions { Id = "CAM1", RtspUrl = "rtsp://192.168.1.3/b" })));

            Assert.Contains("more than once", error.Message);
        }

        [Fact]
        public void TriggeredCamerasShouldDefaultToCommonMotionTopics()
        {
            var triggered = RecordingConfiguration.ToRecordingOptions(new CameraOptions { Id = "cam", Mode = RecordingMode.Triggered, PreRoll = TimeSpan.FromSeconds(3) });
            var custom = RecordingConfiguration.ToRecordingOptions(new CameraOptions
            {
                Id = "cam",
                Mode = RecordingMode.Triggered,
                Events = new CameraEventOptions { TriggerTopics = new List<string> { "Device/Trigger/DigitalInput" } }
            });
            var continuous = RecordingConfiguration.ToRecordingOptions(new CameraOptions { Id = "cam", Events = new CameraEventOptions { Enabled = false } });

            Assert.Equal(CameraRecordingOptions.CommonMotionTopics, triggered.EventTriggers);
            Assert.Equal(TimeSpan.FromSeconds(3), triggered.PreRoll);
            Assert.Equal(new[] { "Device/Trigger/DigitalInput" }, custom.EventTriggers);
            Assert.Empty(continuous.EventTriggers);
            Assert.False(continuous.SubscribeToEvents);
            Assert.Equal(RecordingMode.Continuous, continuous.Mode);
        }

        [Fact]
        public void AnalyticsTriggerClassesShouldReplaceTheMotionDefault()
        {
            var camera = new CameraOptions { Id = "cam", OnvifUrl = "http://192.168.1.10", Mode = RecordingMode.Triggered };
            camera.Analytics.TriggerClasses.Add(" Human ");
            camera.Analytics.MinLikelihood = 0.7;

            var options = RecordingConfiguration.ToRecordingOptions(camera);

            Assert.Equal(new[] { "Human" }, options.AnalyticsTriggers);
            Assert.Equal(0.7, options.AnalyticsMinLikelihood);
            Assert.True(options.UsesAnalytics);
            Assert.Empty(options.EventTriggers);

            var plain = RecordingConfiguration.ToRecordingOptions(new CameraOptions { Id = "cam", OnvifUrl = "http://192.168.1.10" });
            Assert.False(plain.UsesAnalytics);
        }

        [Fact]
        public void AudioShouldMapAndRequireRtspTransport()
        {
            var camera = new CameraOptions { Id = "cam", OnvifUrl = "http://192.168.1.10", Audio = new CameraAudioOptions { Enabled = true } };
            Assert.True(RecordingConfiguration.ToRecordingOptions(camera).Audio);

            var options = new List<CameraOptions>();
            options.Add(new CameraOptions { Id = "udp", OnvifUrl = "http://192.168.1.10", Transport = Onvif.Open.Core.Operations.StreamTransport.Udp, Audio = new CameraAudioOptions { Enabled = true } });

            var error = Assert.Throws<InvalidOperationException>(() => RecordingConfiguration.ValidateCameras(options));
            Assert.Contains("'udp': Audio needs Transport Rtsp", error.Message);
        }

        [Fact]
        public void AnalyticsShouldRequireAnOnvifCameraAndAValidLikelihood()
        {
            var options = new List<CameraOptions>();
            options.Add(new CameraOptions { Id = "rtsp", RtspUrl = "rtsp://192.168.1.11/stream", Analytics = new CameraAnalyticsOptions { Enabled = true } });
            options.Add(new CameraOptions { Id = "onvif", OnvifUrl = "http://192.168.1.10", Analytics = new CameraAnalyticsOptions { Enabled = true, MinLikelihood = 1.5 } });

            var error = Assert.Throws<InvalidOperationException>(() => RecordingConfiguration.ValidateCameras(options));
            Assert.Contains("'rtsp': Analytics need an ONVIF camera", error.Message);
            Assert.Contains("'onvif': Analytics:MinLikelihood", error.Message);
        }

        [Fact]
        public void StreamUriCredentialsShouldBeRedacted()
        {
            Assert.Equal("rtsp://192.168.1.3/stream", RecordingEndpoints.Redact(new Uri("rtsp://admin:secret@192.168.1.3/stream")));
        }

        #endregion
    }
}
