// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Abstract.Interface.Recording;
using Onvif.Open.Core.Events;

namespace Onvif.Open.Recorder.Hosting
{
    /// <summary>
    /// The most recent camera (ONVIF) events, for the API.
    /// </summary>
    public sealed class CameraEventStore : IDisposable
    {
        #region Constructor

        public CameraEventStore(IRecordingManager manager, int capacity = 1000)
        {
            _manager = manager;
            _capacity = capacity;
            if (_manager != null)
            {
                _manager.CameraEventReceived += OnEvent;
            }
        }

        #endregion

        #region Private Variables

        private readonly IRecordingManager _manager;
        private readonly int _capacity;
        private readonly LinkedList<CameraEvent> _events = new LinkedList<CameraEvent>();
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();

        #endregion

        #region Public Methods

        /// <summary>The newest events first; <paramref name="from"/> (inclusive) and <paramref name="to"/> (exclusive) bound the camera time.</summary>
        public IReadOnlyList<CameraEvent> Query(string cameraId, string topic, int limit, DateTimeOffset? from = null, DateTimeOffset? to = null)
        {
            lock (_sync)
            {
                return _events
                    .Where(e => cameraId == null || string.Equals(e.CameraId, cameraId, StringComparison.OrdinalIgnoreCase))
                    .Where(e => topic == null || e.MatchesTopic(topic))
                    .Where(e => !from.HasValue || e.UtcTime >= from.Value)
                    .Where(e => !to.HasValue || e.UtcTime < to.Value)
                    .Take(limit)
                    .ToList();
            }
        }

        public void Dispose()
        {
            if (_manager != null)
            {
                _manager.CameraEventReceived -= OnEvent;
            }
        }

        internal void Add(CameraEvent cameraEvent)
        {
            lock (_sync)
            {
                _events.AddFirst(cameraEvent);
                while (_events.Count > _capacity)
                {
                    _events.RemoveLast();
                }
            }
        }

        #endregion

        #region Private Methods

        private void OnEvent(object sender, CameraEvent cameraEvent) => Add(cameraEvent);

        #endregion
    }
}
