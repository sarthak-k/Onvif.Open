// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Plugin;

namespace Onvif.Open.Plugins.Implementation.Runtime
{
    /// <summary>
    /// Keeps the most recent plugin events in memory (oldest dropped first).
    /// </summary>
    internal sealed class PluginEventStore
    {
        #region Constructor

        public PluginEventStore(int capacity)
        {
            _capacity = capacity;
        }

        #endregion

        #region Private Variables

        private readonly int _capacity;
        private readonly LinkedList<PluginEvent> _events = new LinkedList<PluginEvent>();
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();

        #endregion

        #region Public Methods

        public void Add(PluginEvent pluginEvent)
        {
            lock (_sync)
            {
                _events.AddFirst(pluginEvent);

                while (_events.Count > _capacity)
                {
                    _events.RemoveLast();
                }
            }
        }

        public IReadOnlyList<PluginEvent> Query(PluginEventQuery query)
        {
            query = query ?? new PluginEventQuery();

            lock (_sync)
            {
                return _events
                    .Where(e => query.CameraId == null || string.Equals(e.CameraId, query.CameraId, StringComparison.OrdinalIgnoreCase))
                    .Where(e => query.Type == null || string.Equals(e.Type, query.Type, StringComparison.OrdinalIgnoreCase))
                    .Where(e => query.Source == null || string.Equals(e.Source, query.Source, StringComparison.OrdinalIgnoreCase))
                    .Where(e => !query.Since.HasValue || e.Timestamp >= query.Since.Value)
                    .Where(e => !query.MinimumSeverity.HasValue || e.Severity >= query.MinimumSeverity.Value)
                    .Take(Math.Max(0, query.Limit))
                    .ToList();
            }
        }

        #endregion
    }
}
