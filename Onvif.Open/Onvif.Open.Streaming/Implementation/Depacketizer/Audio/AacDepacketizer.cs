// Sarthak Killedar
// 2026-11-07

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Rtp;

namespace Onvif.Open.Streaming.Implementation.Depacketizer.Audio
{
    /// <summary>
    /// AAC in RTP "mpeg4-generic" (RFC 3640, AAC-hbr / AAC-lbr): a packet carries AU headers and then one or more
    /// access units, or a fragment of one large access unit (the marker bit ends it).
    /// </summary>
    internal sealed class AacDepacketizer : IAudioDepacketizer
    {
        #region Constructor

        public AacDepacketizer(AudioFormat format, int sizeLength, int indexLength, int indexDeltaLength)
        {
            Format = format;
            _sizeLength = sizeLength;
            _indexLength = indexLength;
            _indexDeltaLength = indexDeltaLength;
            _clock = new RtpTimestampClock(format.SampleRate);
        }

        #endregion

        #region Private Variables

        /// <summary>Samples per AAC access unit.</summary>
        private const int SamplesPerFrame = 1024;

        private const int MaxAccessUnitSize = 64 * 1024;

        private readonly int _sizeLength;
        private readonly int _indexLength;
        private readonly int _indexDeltaLength;
        private readonly RtpTimestampClock _clock;
        private const int StackFrames = 32;
        private readonly MemoryStream _fragment = new MemoryStream();
        private uint? _fragmentTimestamp;
        private int _fragmentSize;

        #endregion

        #region Public Variables

        public AudioFormat Format { get; }

        #endregion

        #region Public Methods

        public void Process(RtpPacket packet, ICollection<AudioFrame> frames)
        {
            var payload = packet.Payload.Span;

            if (payload.Length < 2)
            {
                return;
            }

            var headerBits = BinaryPrimitives.ReadUInt16BigEndian(payload);
            var headerBytes = (headerBits + 7) / 8;

            if (payload.Length < 2 + headerBytes || _sizeLength <= 0)
            {
                return;
            }

            // The AU sizes go on the stack (a packet rarely holds more than a few frames); pooled beyond that.
            var maxFrames = (headerBits / _sizeLength) + 1;
            int[] rented = null;
            var sizes = maxFrames <= StackFrames ? stackalloc int[StackFrames] : (rented = ArrayPool<int>.Shared.Rent(maxFrames));
            var count = 0;

            try
            {
                var reader = new BitReader(payload.Slice(2, headerBytes));

                for (var consumed = 0; consumed + _sizeLength <= headerBits;)
                {
                    sizes[count++] = (int)reader.Read(_sizeLength);
                    var indexBits = count == 1 ? _indexLength : _indexDeltaLength;
                    reader.Read(indexBits);
                    consumed += _sizeLength + indexBits;
                }

                var data = payload.Slice(2 + headerBytes);

                if (count == 1 && sizes[0] > data.Length)
                {
                    AddFragment(packet, sizes[0], data, frames);
                    return;
                }

                _fragment.SetLength(0);
                _fragmentTimestamp = null;

                for (var i = 0; i < count && sizes[i] <= data.Length; i++)
                {
                    var timestamp = unchecked(packet.Timestamp + (uint)(i * SamplesPerFrame));
                    frames.Add(new AudioFrame(Format, data.Slice(0, sizes[i]).ToArray(), timestamp, _clock.ToTime(timestamp)));
                    data = data.Slice(sizes[i]);
                }
            }
            finally
            {
                if (rented != null)
                {
                    ArrayPool<int>.Shared.Return(rented);
                }
            }
        }

        #endregion

        #region Private Methods

        private void AddFragment(RtpPacket packet, int size, ReadOnlySpan<byte> data, ICollection<AudioFrame> frames)
        {
            if (_fragmentTimestamp != packet.Timestamp)
            {
                // A new access unit (or the start of the previous one was lost).
                _fragment.SetLength(0);
                _fragmentTimestamp = packet.Timestamp;
                _fragmentSize = size;
            }

            if (_fragment.Length + data.Length > Math.Min(_fragmentSize, MaxAccessUnitSize))
            {
                _fragment.SetLength(0);
                _fragmentTimestamp = null;
                return;
            }

            _fragment.Write(data);

            if (packet.Marker || _fragment.Length == _fragmentSize)
            {
                if (_fragment.Length == _fragmentSize)
                {
                    frames.Add(new AudioFrame(Format, _fragment.ToArray(), packet.Timestamp, _clock.ToTime(packet.Timestamp)));
                }

                _fragment.SetLength(0);
                _fragmentTimestamp = null;
            }
        }

        #endregion

        private ref struct BitReader
        {
            private readonly ReadOnlySpan<byte> _data;
            private int _position;

            public BitReader(ReadOnlySpan<byte> data)
            {
                _data = data;
                _position = 0;
            }

            public uint Read(int bits)
            {
                uint value = 0;

                for (var i = 0; i < bits; i++, _position++)
                {
                    var bit = _position / 8 < _data.Length ? (_data[_position / 8] >> (7 - (_position % 8))) & 1 : 0;
                    value = (value << 1) | (uint)bit;
                }

                return value;
            }
        }
    }
}
