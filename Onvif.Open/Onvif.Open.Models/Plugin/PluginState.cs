// Sarthak Killedar
// 2026-11-07

using Onvif.Open.Core.StateMachines;

namespace Onvif.Open.Core.Plugin
{
    public enum PluginState
    {
        Created = 0,
        Running = 1,

        /// <summary>StartAsync threw: the plugin receives nothing.</summary>
        Faulted = 2,

        Stopped = 3
    }

    public static class PluginStates
    {
        public static StateMachine<PluginState> Machine { get; } = new StateMachine<PluginState>(
            "Plugin",
            PluginState.Created,
            (PluginState.Created, PluginState.Running, "StartAsync succeeded"),
            (PluginState.Created, PluginState.Faulted, "StartAsync threw"),
            (PluginState.Running, PluginState.Stopped, "host stopping"));
    }
}
