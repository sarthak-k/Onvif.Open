// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Depacketizer;
using Onvif.Open.Streaming.Implementation.Depacketizer.Audio;
using Onvif.Open.Streaming.Implementation.Rtp;
using Onvif.Open.Streaming.Implementation.Sdp;

namespace Onvif.Open.Streaming.Implementation.Rtsp
{
    /// <summary>
    /// Reads the video track and, when requested and supported, the audio track, on one timeline: timestamp 0 is
    /// the first video frame, and audio is aligned to it (by the ONVIF capture times when present, otherwise by the
    /// arrival of the first packets).
    /// </summary>
    internal sealed class MediaTrackReader : IRtpTrackReader<MediaFrame>
    {
        #region Constructor

        public MediaTrackReader(bool includeAudio)
        {
            _includeAudio = includeAudio;
        }

        #endregion

        #region Private Variables

        private const int VideoTrack = 0;
        private const int AudioTrack = 1;
        private const int MaxCaptureTimes = 64;

        private readonly bool _includeAudio;
        private readonly List<VideoFrame> _video = new List<VideoFrame>();
        private readonly List<AudioFrame> _audio = new List<AudioFrame>();

        // RTP timestamp -> capture time (ONVIF replay extension) of recent video packets, to stamp the frames.
        private readonly Dictionary<uint, DateTimeOffset> _captureTimes = new Dictionary<uint, DateTimeOffset>();
        private readonly Queue<uint> _captureTimeOrder = new Queue<uint>();

        private IRtpDepacketizer _videoDepacketizer;
        private IAudioDepacketizer _audioDepacketizer;
        private (long Arrival, DateTimeOffset? Capture)? _firstVideo;
        private (long Arrival, DateTimeOffset? Capture)? _firstAudio;
        private TimeSpan? _audioOffset;

        #endregion

        #region Public Variables

        public VideoCodec Codec => _videoDepacketizer?.Codec ?? VideoCodec.Unknown;

        /// <summary>The audio track being read, or null.</summary>
        public AudioFormat Audio => _audioDepacketizer?.Format;

        #endregion

        #region Public Methods

        public IReadOnlyList<SdpMediaDescription> SelectTracks(SessionDescription sessionDescription)
        {
            var video = sessionDescription.Video ?? throw new InvalidOperationException("The RTSP stream does not contain a video track.");
            _videoDepacketizer = RtpDepacketizerFactory.Create(video);

            var audio = _includeAudio ? sessionDescription.Audio : null;
            _audioDepacketizer = audio == null ? null : AudioDepacketizerFactory.Create(audio);

            // An unsupported audio codec is not set up: the video still plays.
            return _audioDepacketizer == null ? new[] { video } : new[] { video, audio };
        }

        public void Process(int track, RtpPacket packet, ICollection<MediaFrame> frames)
        {
            if (track == VideoTrack)
            {
                _firstVideo ??= (Stopwatch.GetTimestamp(), packet.CaptureTime);
                RememberCaptureTime(packet);
                _videoDepacketizer.Process(packet, _video);

                foreach (var frame in _video)
                {
                    frames.Add(_captureTimes.TryGetValue(frame.RtpTimestamp, out var captureTime)
                        ? new VideoFrame(frame.Codec, frame.Data, frame.IsKeyFrame, frame.RtpTimestamp, frame.Timestamp, captureTime)
                        : frame);
                }

                _video.Clear();
            }
            else if (track == AudioTrack && _audioDepacketizer != null)
            {
                _firstAudio ??= (Stopwatch.GetTimestamp(), packet.CaptureTime);
                _audioDepacketizer.Process(packet, _audio);

                foreach (var frame in _audio)
                {
                    var aligned = Align(frame);
                    if (aligned != null)
                    {
                        frames.Add(packet.CaptureTime.HasValue ? aligned.WithCaptureTime(packet.CaptureTime.Value) : aligned);
                    }
                }

                _audio.Clear();
            }
        }

        public DateTimeOffset? GetCaptureTime(MediaFrame frame)
        {
            return frame.CaptureTime;
        }

        #endregion

        #region Private Methods

        /// <summary>Moves an audio frame onto the video timeline; null before the first video frame.</summary>
        private AudioFrame Align(AudioFrame frame)
        {
            if (!_audioOffset.HasValue)
            {
                if (!_firstVideo.HasValue)
                {
                    // No timeline yet: audio before the first video packet is dropped.
                    return null;
                }

                var video = _firstVideo.Value;
                var audio = _firstAudio.Value;
                _audioOffset = video.Capture.HasValue && audio.Capture.HasValue
                    ? audio.Capture.Value - video.Capture.Value
                    : Stopwatch.GetElapsedTime(video.Arrival, audio.Arrival);
            }

            var timestamp = frame.Timestamp + _audioOffset.Value;
            return timestamp < TimeSpan.Zero ? null : frame.WithTimestamp(timestamp);
        }

        private void RememberCaptureTime(in RtpPacket packet)
        {
            if (!packet.CaptureTime.HasValue || _captureTimes.ContainsKey(packet.Timestamp))
            {
                return;
            }

            _captureTimes[packet.Timestamp] = packet.CaptureTime.Value;
            _captureTimeOrder.Enqueue(packet.Timestamp);

            while (_captureTimeOrder.Count > MaxCaptureTimes)
            {
                _captureTimes.Remove(_captureTimeOrder.Dequeue());
            }
        }

        #endregion
    }
}
