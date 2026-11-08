// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;

namespace Onvif.Open.Recording.Implementation.Recorder
{
    /// <summary>
    /// Decides whether a camera in triggered mode records right now. Stateful sources (e.g. motion on/off)
    /// hold the trigger while active; pulses (plugins, the API, stateless events) hold it for a duration.
    /// After the last activity the trigger stays on for the post-roll.
    /// </summary>
    internal sealed class RecordingTrigger
    {
        #region Constructor

        public RecordingTrigger(TimeSpan postRoll)
        {
            _postRoll = postRoll < TimeSpan.Zero ? TimeSpan.Zero : postRoll;
        }

        #endregion

        #region Private Variables

        private readonly TimeSpan _postRoll;
        private readonly HashSet<string> _activeSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Threading.Lock _sync = new System.Threading.Lock();
        private DateTimeOffset _activeUntil = DateTimeOffset.MinValue;

        #endregion

        #region Public Variables

        public DateTimeOffset? LastTriggeredAt { get; private set; }

        #endregion

        #region Public Methods

        public void Activate(string source, DateTimeOffset now)
        {
            lock (_sync)
            {
                _activeSources.Add(source ?? string.Empty);
                LastTriggeredAt = now;
            }
        }

        public void Deactivate(string source, DateTimeOffset now)
        {
            lock (_sync)
            {
                if (_activeSources.Remove(source ?? string.Empty))
                {
                    Extend(now + _postRoll);
                }
            }
        }

        public void Pulse(TimeSpan duration, DateTimeOffset now)
        {
            lock (_sync)
            {
                Extend(now + (duration < TimeSpan.Zero ? TimeSpan.Zero : duration) + _postRoll);
                LastTriggeredAt = now;
            }
        }

        public bool IsActive(DateTimeOffset now)
        {
            lock (_sync)
            {
                return _activeSources.Count > 0 || now < _activeUntil;
            }
        }

        #endregion

        #region Private Methods

        private void Extend(DateTimeOffset until)
        {
            if (until > _activeUntil)
            {
                _activeUntil = until;
            }
        }

        #endregion
    }
}
