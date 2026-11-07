// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Core.Abstract.Interface.Analytics
{
    /// <summary>
    /// The analytics metadata track of a camera's RTSP stream: objects the camera detects, frame by frame.
    /// Dispose it to end the session.
    /// </summary>
    public interface IAnalyticsStream : IAsyncDisposable
    {
        #region Public Variables

        Uri StreamUri { get; }

        StreamTransport Transport { get; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Analytics frames as they arrive. Ends when the stream stops; throws when the connection fails.
        /// Slow readers lose the oldest frames rather than delaying the camera.
        /// </summary>
        IAsyncEnumerable<AnalyticsFrame> ReadFramesAsync(CancellationToken cancellationToken = default);

        Task StopAsync(CancellationToken cancellationToken = default);

        #endregion
    }
}
