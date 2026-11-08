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
            _manager.CameraEventReceived += OnEvent;
        }

        #endregion

        #region Private Variables

        private readonly IRecordingManager _manager;
        private readonly int _capacity;
        private readonly LinkedList<CameraEvent> _events = new LinkedList<CameraEvent>();
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();

        #endregion

        #region Public Methods

        /// <summary>Newest first.</summary>
        public IReadOnlyList<CameraEvent> Query(string cameraId, string topic, int limit)
        {
            lock (_sync)
            {
                return _events
                    .Where(e => cameraId == null || string.Equals(e.CameraId, cameraId, StringComparison.OrdinalIgnoreCase))
                    .Where(e => topic == null || e.MatchesTopic(topic))
                    .Take(limit)
                    .ToList();
            }
        }

        public void Dispose()
        {
            _manager.CameraEventReceived -= OnEvent;
        }

        #endregion

        #region Private Methods

        private void OnEvent(object sender, CameraEvent cameraEvent)
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
    }
}
