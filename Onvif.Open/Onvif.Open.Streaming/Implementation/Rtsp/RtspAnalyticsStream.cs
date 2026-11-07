// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Analytics;
using Onvif.Open.Core.Analytics;
using Onvif.Open.Core.Operations;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// The ONVIF metadata track of an <see cref="RtspSession{TFrame}"/>.
    /// </summary>
    internal sealed class RtspAnalyticsStream : IAnalyticsStream
    {
        #region Constructor

        private RtspAnalyticsStream(RtspSession<AnalyticsFrame> session)
        {
            _session = session;
        }

        #endregion

        #region Private Variables

        private readonly RtspSession<AnalyticsFrame> _session;

        #endregion

        #region Public Variables

        public Uri StreamUri => _session.StreamUri;
        public StreamTransport Transport => _session.Transport;

        #endregion

        #region Public Methods

        internal static async Task<IAnalyticsStream> StartAsync(Uri streamUri, NetworkCredential credential, StreamTransport transport, TimeSpan timeout, int frameBufferCapacity, CancellationToken cancellationToken)
        {
            var session = await RtspSession<AnalyticsFrame>.StartAsync(new MetadataTrackReader(), streamUri, credential, transport, timeout, frameBufferCapacity, cancellationToken).ConfigureAwait(false);
            return new RtspAnalyticsStream(session);
        }

        public IAsyncEnumerable<AnalyticsFrame> ReadFramesAsync(CancellationToken cancellationToken = default)
        {
            return _session.ReadFramesAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return _session.StopAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            return _session.DisposeAsync();
        }

        #endregion
    }
}
