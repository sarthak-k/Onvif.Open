// Sarthak Killedar
// 2026-11-07

using System;
using System.IO;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Recording.Implementation.Format
{
    /// <summary>
    /// Minimal MPEG-2 transport stream muxer (ISO/IEC 13818-1) for one H.264/H.265 video track:
    /// PAT/PMT before every key frame, one PES per access unit with PTS, PCR on the video PID.
    /// </summary>
    internal sealed class MpegTsMuxer : ISegmentMuxer
    {
        #region Constructor

        public MpegTsMuxer(Stream output, VideoCodec codec)
        {
            _output = output;
            _codec = codec;
            _streamType = codec == VideoCodec.H265 ? (byte)0x24 : (byte)0x1B;
        }

        #endregion

        #region Private Variables

        internal const int PacketSize = 188;
        internal const int VideoPid = 0x0100;
        internal const int PmtPid = 0x1000;

        /// <summary>Timestamps start at one second so the PCR (which precedes the PTS) never goes negative.</summary>
        internal const long PtsOffset = 90000;
        private const long PcrDelay = 9000;

        private static readonly byte[] H264AccessUnitDelimiter = { 0, 0, 0, 1, 0x09, 0xF0 };
        private static readonly byte[] H265AccessUnitDelimiter = { 0, 0, 0, 1, 0x46, 0x01, 0x50 };

        private readonly Stream _output;
        private readonly VideoCodec _codec;
        private readonly byte _streamType;
        private readonly byte[] _packet = new byte[PacketSize];
        private int _patCounter;
        private int _pmtCounter;
        private int _videoCounter;
        private bool _started;
        private TimeSpan _firstTimestamp;

        #endregion

        #region Public Methods

        public void Write(VideoFrame frame)
        {
            if (!_started)
            {
                _started = true;
                _firstTimestamp = frame.Timestamp;
            }

            if (frame.IsKeyFrame || _patCounter == 0)
            {
                WritePsi(0, ref _patCounter, BuildPat());
                WritePsi(PmtPid, ref _pmtCounter, BuildPmt());
            }

            var elapsed = frame.Timestamp - _firstTimestamp;
            var pts = PtsOffset + Math.Max(0, elapsed.Ticks * 90000 / TimeSpan.TicksPerSecond);

            WritePes(frame.Data.Span, pts, frame.IsKeyFrame);
        }

        public void Dispose()
        {
            _output.Flush();
        }

        internal static uint Crc32(ReadOnlySpan<byte> data)
        {
            // CRC-32/MPEG-2: polynomial 0x04C11DB7, initial value 0xFFFFFFFF, no reflection, no final xor.
            var crc = 0xFFFFFFFFu;

            foreach (var value in data)
            {
                crc ^= (uint)value << 24;
                for (var bit = 0; bit < 8; bit++)
                {
                    crc = (crc & 0x80000000u) != 0 ? (crc << 1) ^ 0x04C11DB7u : crc << 1;
                }
            }

            return crc;
        }

        #endregion

        #region Private Methods

        private byte[] BuildPat()
        {
            return Section(new byte[]
            {
                0x00,                                  // table_id: program_association_section
                0xB0, 0x0D,                            // section_syntax_indicator, section_length = 13
                0x00, 0x01,                            // transport_stream_id
                0xC1,                                  // version 0, current_next_indicator
                0x00, 0x00,                            // section_number, last_section_number
                0x00, 0x01,                            // program_number 1
                (byte)(0xE0 | (PmtPid >> 8)), (byte)(PmtPid & 0xFF)
            });
        }

        private byte[] BuildPmt()
        {
            return Section(new byte[]
            {
                0x02,                                  // table_id: TS_program_map_section
                0xB0, 0x12,                            // section_length = 18
                0x00, 0x01,                            // program_number 1
                0xC1,
                0x00, 0x00,
                (byte)(0xE0 | (VideoPid >> 8)), (byte)(VideoPid & 0xFF),   // PCR_PID
                0xF0, 0x00,                            // program_info_length 0
                _streamType,
                (byte)(0xE0 | (VideoPid >> 8)), (byte)(VideoPid & 0xFF),
                0xF0, 0x00                             // ES_info_length 0
            });
        }

        private static byte[] Section(byte[] body)
        {
            var crc = Crc32(body);
            var section = new byte[body.Length + 4];
            Buffer.BlockCopy(body, 0, section, 0, body.Length);
            section[body.Length] = (byte)(crc >> 24);
            section[body.Length + 1] = (byte)(crc >> 16);
            section[body.Length + 2] = (byte)(crc >> 8);
            section[body.Length + 3] = (byte)crc;
            return section;
        }

        private void WritePsi(int pid, ref int counter, byte[] section)
        {
            Array.Fill(_packet, (byte)0xFF);
            _packet[0] = 0x47;
            _packet[1] = (byte)(0x40 | (pid >> 8));
            _packet[2] = (byte)pid;
            _packet[3] = (byte)(0x10 | (counter & 0x0F));
            _packet[4] = 0x00; // pointer_field
            Buffer.BlockCopy(section, 0, _packet, 5, section.Length);
            counter++;
            _output.Write(_packet, 0, PacketSize);
        }

        private void WritePes(ReadOnlySpan<byte> accessUnit, long pts, bool isKeyFrame)
        {
            var delimiter = _codec == VideoCodec.H265 ? H265AccessUnitDelimiter : H264AccessUnitDelimiter;
            var needsDelimiter = !StartsWith(accessUnit, delimiter.AsSpan(0, delimiter.Length - 1));

            var header = new byte[14];
            header[0] = 0x00;
            header[1] = 0x00;
            header[2] = 0x01;
            header[3] = 0xE0;                          // stream_id: video
            header[4] = 0x00;                          // PES_packet_length 0: unbounded (allowed for video)
            header[5] = 0x00;
            header[6] = 0x80;                          // marker bits
            header[7] = 0x80;                          // PTS only
            header[8] = 0x05;                          // PES_header_data_length
            header[9] = (byte)(0x21 | ((pts >> 29) & 0x0E));
            header[10] = (byte)(pts >> 22);
            header[11] = (byte)(((pts >> 14) & 0xFE) | 0x01);
            header[12] = (byte)(pts >> 7);
            header[13] = (byte)(((pts << 1) & 0xFE) | 0x01);

            var length = header.Length + (needsDelimiter ? delimiter.Length : 0) + accessUnit.Length;
            var pes = new byte[length];
            header.CopyTo(pes, 0);
            var offset = header.Length;

            if (needsDelimiter)
            {
                delimiter.CopyTo(pes, offset);
                offset += delimiter.Length;
            }

            accessUnit.CopyTo(pes.AsSpan(offset));

            var remaining = pes.AsSpan();
            var first = true;

            while (!remaining.IsEmpty)
            {
                var consumed = WriteVideoPacket(remaining, first, first ? pts - PcrDelay : (long?)null, first && isKeyFrame);
                remaining = remaining.Slice(consumed);
                first = false;
            }
        }

        private int WriteVideoPacket(ReadOnlySpan<byte> payload, bool payloadUnitStart, long? pcr, bool randomAccess)
        {
            var adaptationBody = pcr.HasValue || randomAccess ? 1 + (pcr.HasValue ? 6 : 0) : 0;
            var hasAdaptation = adaptationBody > 0;
            var space = PacketSize - 4 - (hasAdaptation ? 1 + adaptationBody : 0);
            var take = Math.Min(space, payload.Length);
            var stuffing = space - take;

            if (stuffing > 0 && !hasAdaptation)
            {
                // An adaptation field is needed just for stuffing: its length byte takes one byte, its flags another.
                hasAdaptation = true;
                stuffing--;
                if (stuffing > 0)
                {
                    adaptationBody = 1;
                    stuffing--;
                }
            }

            _packet[0] = 0x47;
            _packet[1] = (byte)((payloadUnitStart ? 0x40 : 0x00) | (VideoPid >> 8));
            _packet[2] = (byte)(VideoPid & 0xFF);
            _packet[3] = (byte)((hasAdaptation ? 0x30 : 0x10) | (_videoCounter & 0x0F));
            _videoCounter++;

            var index = 4;

            if (hasAdaptation)
            {
                _packet[index++] = (byte)(adaptationBody + stuffing);

                if (adaptationBody > 0)
                {
                    _packet[index++] = (byte)((randomAccess ? 0x40 : 0x00) | (pcr.HasValue ? 0x10 : 0x00));

                    if (pcr.HasValue)
                    {
                        var pcrBase = pcr.Value;
                        _packet[index++] = (byte)(pcrBase >> 25);
                        _packet[index++] = (byte)(pcrBase >> 17);
                        _packet[index++] = (byte)(pcrBase >> 9);
                        _packet[index++] = (byte)(pcrBase >> 1);
                        _packet[index++] = (byte)(((pcrBase & 1) << 7) | 0x7E);
                        _packet[index++] = 0x00;
                    }
                }

                for (var i = 0; i < stuffing; i++)
                {
                    _packet[index++] = 0xFF;
                }
            }

            payload.Slice(0, take).CopyTo(_packet.AsSpan(index));
            _output.Write(_packet, 0, PacketSize);
            return take;
        }

        private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix)
        {
            return data.Length >= prefix.Length && data.Slice(0, prefix.Length).SequenceEqual(prefix);
        }

        #endregion
    }
}
