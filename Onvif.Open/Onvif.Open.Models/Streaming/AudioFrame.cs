// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Streaming
{
    /// <summary>
    /// One encoded audio frame: an AAC access unit (raw, without ADTS header), or a block of G.711 samples
    /// (decode with <see cref="G711"/>).
    /// </summary>
    public sealed class AudioFrame : MediaFrame
    {
        public AudioFrame(AudioFormat format, ReadOnlyMemory<byte> data, uint rtpTimestamp, TimeSpan timestamp, DateTimeOffset? captureTime = null)
            : base(data, rtpTimestamp, timestamp, captureTime)
        {
            Format = format ?? throw new ArgumentNullException(nameof(format));
        }

        public AudioFormat Format { get; }

        public AudioCodec Codec => Format.Codec;

        /// <summary>The same frame on another timeline position (used to align audio with video).</summary>
        public AudioFrame WithTimestamp(TimeSpan timestamp)
        {
            return new AudioFrame(Format, Data, RtpTimestamp, timestamp, CaptureTime);
        }

        /// <summary>The same frame with its recording time.</summary>
        public AudioFrame WithCaptureTime(DateTimeOffset captureTime)
        {
            return new AudioFrame(Format, Data, RtpTimestamp, Timestamp, captureTime);
        }
    }

    /// <summary>
    /// Audio codec and parameters of a stream's audio track.
    /// </summary>
    public sealed class AudioFormat
    {
        public AudioFormat(AudioCodec codec, int sampleRate, int channels, ReadOnlyMemory<byte> config = default)
        {
            Codec = codec;
            SampleRate = sampleRate;
            Channels = channels;
            Config = config;
        }

        public AudioCodec Codec { get; }
        public int SampleRate { get; }
        public int Channels { get; }

        /// <summary>AAC: the AudioSpecificConfig (ISO/IEC 14496-3) from the session description; empty otherwise.</summary>
        public ReadOnlyMemory<byte> Config { get; }

        public override string ToString()
        {
            return $"{Codec} {SampleRate} Hz, {Channels} channel(s)";
        }
    }

    public enum AudioCodec
    {
        Unknown = 0,

        /// <summary>AAC (RTP "mpeg4-generic", RFC 3640).</summary>
        Aac = 1,

        /// <summary>G.711 µ-law (RTP payload type 0, "PCMU").</summary>
        Pcmu = 2,

        /// <summary>G.711 A-law (RTP payload type 8, "PCMA").</summary>
        Pcma = 3
    }
}
