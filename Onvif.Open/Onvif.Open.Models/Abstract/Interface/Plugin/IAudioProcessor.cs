// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Plugin;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Receives the audio of the plugin's cameras (cameras with "Audio": { "Enabled": true }), e.g. for glass break or
    /// shouting detection. Delivered continuously, also while a triggered camera is not recording. When the plugin
    /// cannot keep up the oldest queued frames are dropped.
    /// </summary>
    public interface IAudioProcessor : IPlugin
    {
        #region Public Methods

        ValueTask ProcessAudioAsync(CameraAudio audio, CancellationToken cancellationToken);

        #endregion
    }
}
