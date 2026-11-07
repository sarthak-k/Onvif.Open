// Sarthak Killedar
// 2026-11-07

using System;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Core.Plugin
{
    /// <summary>
    /// An audio frame of a camera, for audio analytics (e.g. glass break or shouting detection).
    /// </summary>
    public sealed class CameraAudio
    {
        #region Constructor

        public CameraAudio(string cameraId, AudioFrame frame, DateTimeOffset receivedAt)
        {
            CameraId = cameraId;
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            ReceivedAt = receivedAt;
        }

        #endregion

        #region Public Variables

        public string CameraId { get; }

        /// <summary>The encoded audio; G.711 decodes to PCM with <see cref="G711.DecodeToPcm16"/>.</summary>
        public AudioFrame Frame { get; }

        public DateTimeOffset ReceivedAt { get; }

        #endregion
    }
}
