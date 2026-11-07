// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Recording;

namespace Onvif.Open.Core.Plugin
{
    public sealed class CameraStateChange
    {
        #region Constructor

        public CameraStateChange(string cameraId, RecordingState previousState, RecordingState state, string error, DateTimeOffset timestamp)
        {
            CameraId = cameraId;
            PreviousState = previousState;
            State = state;
            Error = error;
            Timestamp = timestamp;
        }

        #endregion

        #region Public Variables

        public string CameraId { get; }
        public RecordingState PreviousState { get; }
        public RecordingState State { get; }

        /// <summary>Why the camera left the Recording/Connecting state, when it failed.</summary>
        public string Error { get; }

        public DateTimeOffset Timestamp { get; }

        /// <summary>True when the camera stopped delivering video because of a failure (not a requested stop).</summary>
        public bool IsConnectionLost => PreviousState == RecordingState.Recording && State == RecordingState.Reconnecting;

        /// <summary>True when video started flowing (again).</summary>
        public bool IsConnectionEstablished => State == RecordingState.Recording;

        #endregion
    }
}
