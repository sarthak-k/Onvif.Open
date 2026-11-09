// Sarthak Killedar
// 2026-11-09

using System;
using Onvif.Open.Core.Recording;
using Onvif.Open.Recorder.Tenancy;

namespace Onvif.Open.Recorder.Events
{
    /// <summary>
    /// When a recorded file has been kept long enough, by its camera's recording policy: a camera recording only around
    /// events keeps everything for the event retention; a camera recording always keeps files for the continuous
    /// retention, and files overlapping an event (with a margin) for the event retention.
    /// </summary>
    public sealed class PolicyRetention
    {
        public PolicyRetention(CameraDirectory directory, EventIndex events)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _events = events ?? throw new ArgumentNullException(nameof(events));
        }

        private readonly CameraDirectory _directory;
        private readonly EventIndex _events;

        /// <summary>False for cameras this recorder does not know (removed cameras keep their files until the recorder-wide limits apply).</summary>
        public bool IsExpired(RecordingSegment segment, DateTimeOffset now)
        {
            var camera = _directory.Find(segment.CameraId);
            if (camera == null)
            {
                return false;
            }

            var age = now - segment.LastWriteTime;

            if (age > camera.EventRetention)
            {
                return true;
            }

            if (camera.EventsOnly || age <= camera.ContinuousRetention)
            {
                return false;
            }

            return !_events.Overlaps(segment.CameraId, segment.StartTime, segment.LastWriteTime);
        }
    }
}
