// Sarthak Killedar
// 2026-11-07

using Onvif.Open.Core.StateMachines;

namespace Onvif.Open.Core.Recording
{
    public enum RecordingState
    {
        Stopped = 0,
        Connecting = 1,
        Recording = 2,
        Reconnecting = 3
    }

    public static class RecordingStates
    {
        /// <summary>A camera's recording session; any move but to Stopped comes from the recording loop.</summary>
        public static StateMachine<RecordingState> Machine { get; } = new StateMachine<RecordingState>(
            "Camera recording",
            RecordingState.Stopped,
            (RecordingState.Stopped, RecordingState.Connecting, "started"),
            (RecordingState.Connecting, RecordingState.Recording, "first frame"),
            (RecordingState.Connecting, RecordingState.Reconnecting, "connection failed"),
            (RecordingState.Connecting, RecordingState.Stopped, "stopped"),
            (RecordingState.Recording, RecordingState.Reconnecting, "stream lost or stalled"),
            (RecordingState.Recording, RecordingState.Stopped, "stopped"),
            (RecordingState.Reconnecting, RecordingState.Connecting, "retry after a delay"),
            (RecordingState.Reconnecting, RecordingState.Stopped, "stopped"));
    }
}
