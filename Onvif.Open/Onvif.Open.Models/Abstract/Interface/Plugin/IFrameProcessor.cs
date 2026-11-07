// Sarthak Killedar
// 2026-11-07

using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Plugin;

namespace Onvif.Open.Core.Abstract.Interface.Plugin
{
    /// <summary>
    /// Receives the encoded video frames of the plugin's cameras, e.g. for video analytics.
    /// </summary>
    /// <remarks>
    /// Frames are queued per plugin; when the plugin cannot keep up the oldest queued frames are dropped
    /// (recording is never slowed down). Use <see cref="FrameFilter.KeyFramesOnly"/> when decoding one
    /// picture every GOP is enough.
    /// </remarks>
    public interface IFrameProcessor : IPlugin
    {
        #region Public Variables

        FrameFilter FrameFilter { get; }

        #endregion

        #region Public Methods

        ValueTask ProcessFrameAsync(CameraFrame frame, CancellationToken cancellationToken);

        #endregion
    }
}
