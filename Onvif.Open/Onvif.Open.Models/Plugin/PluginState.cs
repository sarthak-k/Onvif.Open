// Sarthak Killedar
// 2026-11-07

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
}
