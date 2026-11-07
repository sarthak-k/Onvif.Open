// Sarthak Killedar
// 2026-11-07

using System.Collections.Generic;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Rtp;

namespace Onvif.Open.Streaming.Implementation.Depacketizer.Audio
{
    /// <summary>
    /// Turns the RTP packets of an audio track into <see cref="AudioFrame"/>s (timestamps relative to the first packet).
    /// </summary>
    internal interface IAudioDepacketizer
    {
        #region Public Variables

        AudioFormat Format { get; }

        #endregion

        #region Public Methods

        void Process(RtpPacket packet, ICollection<AudioFrame> frames);

        #endregion
    }
}
