// Sarthak Killedar
// 2026-11-07

using System;
using System.Buffers.Binary;

namespace Onvif.Open.Streaming.Implementation.Rtp
{
    /// <summary>
    /// A parsed RTP packet (RFC 3550).
    /// </summary>
    internal readonly struct RtpPacket
    {
        #region Constructor

        private RtpPacket(bool marker, byte payloadType, ushort sequenceNumber, uint timestamp, uint ssrc, ReadOnlyMemory<byte> payload, DateTimeOffset? captureTime)
        {
            CaptureTime = captureTime;
            Marker = marker;
            PayloadType = payloadType;
            SequenceNumber = sequenceNumber;
            Timestamp = timestamp;
            Ssrc = ssrc;
            Payload = payload;
        }

        #endregion

        #region Public Variables

        public const int HeaderSize = 12;

        /// <summary>RTP header extension profile of the ONVIF replay extension (ONVIF Streaming Specification).</summary>
        public const ushort OnvifReplayExtension = 0xABAC;

        private static readonly DateTimeOffset NtpEpoch = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public bool Marker { get; }
        public byte PayloadType { get; }
        public ushort SequenceNumber { get; }
        public uint Timestamp { get; }
        public uint Ssrc { get; }
        public ReadOnlyMemory<byte> Payload { get; }

        /// <summary>Absolute time from the ONVIF replay header extension (NTP timestamp), when present.</summary>
        public DateTimeOffset? CaptureTime { get; }

        #endregion

        #region Public Methods

        public static bool TryParse(ReadOnlyMemory<byte> data, out RtpPacket packet)
        {
            packet = default;
            var span = data.Span;

            if (span.Length < HeaderSize || (span[0] >> 6) != 2)
            {
                return false;
            }

            var hasPadding = (span[0] & 0x20) != 0;
            var hasExtension = (span[0] & 0x10) != 0;
            var csrcCount = span[0] & 0x0F;
            var offset = HeaderSize + (csrcCount * 4);
            DateTimeOffset? captureTime = null;

            if (hasExtension)
            {
                if (span.Length < offset + 4)
                {
                    return false;
                }

                var profile = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset, 2));
                var extensionWords = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(offset + 2, 2));

                if (profile == OnvifReplayExtension && extensionWords >= 2 && span.Length >= offset + 12)
                {
                    var seconds = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset + 4, 4));
                    var fraction = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset + 8, 4));
                    captureTime = NtpEpoch.AddSeconds(seconds).AddTicks((long)(fraction * (double)TimeSpan.TicksPerSecond / 4294967296.0));
                }

                offset += 4 + (extensionWords * 4);
            }

            var end = span.Length;

            if (hasPadding && end > offset)
            {
                end -= span[end - 1];
            }

            if (end < offset)
            {
                return false;
            }

            packet = new RtpPacket(
                (span[1] & 0x80) != 0,
                (byte)(span[1] & 0x7F),
                BinaryPrimitives.ReadUInt16BigEndian(span.Slice(2, 2)),
                BinaryPrimitives.ReadUInt32BigEndian(span.Slice(4, 4)),
                BinaryPrimitives.ReadUInt32BigEndian(span.Slice(8, 4)),
                data.Slice(offset, end - offset),
                captureTime);

            return true;
        }

        #endregion
    }
}
