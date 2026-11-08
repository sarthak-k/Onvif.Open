// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Recording.Implementation.Format
{
    /// <summary>
    /// Reads back MPEG-TS files written by <see cref="MpegTsMuxer"/> (or any TS with one H.264/H.265 and one ADTS AAC
    /// stream): yields the video access units and AAC frames with their PTS and byte offsets.
    /// </summary>
    internal sealed class MpegTsDemuxer
    {
        #region Private Variables

        private const int PacketSize = MpegTsMuxer.PacketSize;
        private const int MaxPesSize = 32 * 1024 * 1024;

        private readonly Dictionary<int, PesBuffer> _buffers = new Dictionary<int, PesBuffer>();
        private int _pmtPid = -1;
        private int _videoPid = -1;
        private int _audioPid = -1;
        private long _lastPatOffset;

        #endregion

        #region Public Variables

        public VideoCodec Codec { get; private set; }

        #endregion

        #region Public Methods

        /// <summary>The frames of <paramref name="input"/>; an incomplete last packet (file still being written) is ignored.</summary>
        public async IAsyncEnumerable<DemuxedFrame> ReadAsync(Stream input, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var packet = new byte[PacketSize];
            long offset = 0;

            while (await ReadPacketAsync(input, packet, cancellationToken).ConfigureAwait(false))
            {
                if (packet[0] == 0x47)
                {
                    var frame = Process(packet, offset);
                    if (frame != null)
                    {
                        yield return frame;
                    }
                }

                offset += PacketSize;
            }

            foreach (var pid in new[] { _videoPid, _audioPid })
            {
                if (pid >= 0 && _buffers.TryGetValue(pid, out var buffer))
                {
                    var frame = Complete(pid, buffer);
                    if (frame != null)
                    {
                        yield return frame;
                    }
                }
            }
        }

        #endregion

        #region Private Methods

        private static async Task<bool> ReadPacketAsync(Stream input, byte[] packet, CancellationToken cancellationToken)
        {
            var read = 0;

            while (read < PacketSize)
            {
                var count = await input.ReadAsync(packet.AsMemory(read, PacketSize - read), cancellationToken).ConfigureAwait(false);
                if (count == 0)
                {
                    return false;
                }

                read += count;
            }

            return true;
        }

        private DemuxedFrame Process(byte[] packet, long offset)
        {
            var pid = ((packet[1] & 0x1F) << 8) | packet[2];
            var unitStart = (packet[1] & 0x40) != 0;
            var adaptation = (packet[3] >> 4) & 0x03;
            var index = 4;

            if (adaptation == 2 || adaptation == 3)
            {
                index += 1 + packet[4];
            }

            if (adaptation == 2 || index >= PacketSize)
            {
                return null;
            }

            var payload = packet.AsSpan(index);

            if (pid == 0)
            {
                _lastPatOffset = offset;
                if (unitStart)
                {
                    ParsePat(payload);
                }

                return null;
            }

            if (pid == _pmtPid)
            {
                if (unitStart)
                {
                    ParsePmt(payload);
                }

                return null;
            }

            if (pid != _videoPid && pid != _audioPid)
            {
                return null;
            }

            DemuxedFrame completed = null;

            if (unitStart)
            {
                if (_buffers.TryGetValue(pid, out var previous))
                {
                    completed = Complete(pid, previous);
                }

                _buffers[pid] = new PesBuffer(pid == _videoPid ? _lastPatOffset : offset);
            }

            if (_buffers.TryGetValue(pid, out var buffer) && buffer.Data.Length + payload.Length <= MaxPesSize)
            {
                buffer.Data.Write(payload);
            }

            return completed;
        }

        private DemuxedFrame Complete(int pid, PesBuffer buffer)
        {
            _buffers.Remove(pid);
            var pes = buffer.Data.GetBuffer().AsSpan(0, (int)buffer.Data.Length);

            if (pes.Length < 9 || pes[0] != 0 || pes[1] != 0 || pes[2] != 1 || (pes[7] & 0x80) == 0)
            {
                return null;
            }

            var pts = ((long)(pes[9] & 0x0E) << 29) | ((long)pes[10] << 22) | ((long)(pes[11] & 0xFE) << 14) | ((long)pes[12] << 7) | ((long)pes[13] >> 1);
            var data = pes.Slice(9 + pes[8]).ToArray();

            return pid == _videoPid
                ? new DemuxedFrame(pts, buffer.Offset, data, IsKeyFrame(data, Codec), null)
                : new DemuxedFrame(pts, buffer.Offset, data, false, AudioFormatOf(data));
        }

        private void ParsePat(ReadOnlySpan<byte> payload)
        {
            var section = payload.Slice(1 + payload[0]);
            if (section.Length >= 12)
            {
                _pmtPid = ((section[10] & 0x1F) << 8) | section[11];
            }
        }

        private void ParsePmt(ReadOnlySpan<byte> payload)
        {
            var section = payload.Slice(1 + payload[0]);
            if (section.Length < 12)
            {
                return;
            }

            var sectionLength = ((section[1] & 0x0F) << 8) | section[2];
            var end = Math.Min(section.Length, 3 + sectionLength - 4);
            var position = 12 + (((section[10] & 0x0F) << 8) | section[11]);

            while (position + 5 <= end)
            {
                var streamType = section[position];
                var esPid = ((section[position + 1] & 0x1F) << 8) | section[position + 2];
                var infoLength = ((section[position + 3] & 0x0F) << 8) | section[position + 4];

                switch (streamType)
                {
                    case 0x1B:
                        _videoPid = esPid;
                        Codec = VideoCodec.H264;
                        break;
                    case 0x24:
                        _videoPid = esPid;
                        Codec = VideoCodec.H265;
                        break;
                    case 0x0F:
                        _audioPid = esPid;
                        break;
                }

                position += 5 + infoLength;
            }
        }

        private static bool IsKeyFrame(ReadOnlySpan<byte> accessUnit, VideoCodec codec)
        {
            for (var i = 0; i + 3 < accessUnit.Length; i++)
            {
                if (accessUnit[i] != 0 || accessUnit[i + 1] != 0 || accessUnit[i + 2] != 1)
                {
                    continue;
                }

                var header = accessUnit[i + 3];
                if (codec == VideoCodec.H265 ? ((header >> 1) & 0x3F) is >= 16 and <= 21 : (header & 0x1F) == 5)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>AudioFormat from the ADTS header (with an AudioSpecificConfig rebuilt from it).</summary>
        private static AudioFormat AudioFormatOf(ReadOnlySpan<byte> adts)
        {
            if (adts.Length < 7 || adts[0] != 0xFF || (adts[1] & 0xF0) != 0xF0)
            {
                return null;
            }

            var objectType = (adts[2] >> 6) + 1;
            var frequencyIndex = (adts[2] >> 2) & 0x0F;
            var channels = ((adts[2] & 0x01) << 2) | (adts[3] >> 6);
            int[] rates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };

            if (frequencyIndex >= rates.Length)
            {
                return null;
            }

            var config = new[] { (byte)((objectType << 3) | (frequencyIndex >> 1)), (byte)(((frequencyIndex & 1) << 7) | (channels << 3)) };
            return new AudioFormat(AudioCodec.Aac, rates[frequencyIndex], channels, config);
        }

        #endregion

        private sealed class PesBuffer
        {
            public PesBuffer(long offset)
            {
                Offset = offset;
            }

            public long Offset { get; }
            public MemoryStream Data { get; } = new MemoryStream();
        }
    }

    /// <summary>
    /// A frame read from an MPEG-TS file: video access unit (Annex-B) or ADTS frame(s) with <see cref="Audio"/> set.
    /// </summary>
    internal sealed class DemuxedFrame
    {
        public DemuxedFrame(long pts, long offset, byte[] data, bool isKeyFrame, AudioFormat audio)
        {
            Pts = pts;
            Offset = offset;
            Data = data;
            IsKeyFrame = isKeyFrame;
            Audio = audio;
        }

        /// <summary>90 kHz presentation time stamp.</summary>
        public long Pts { get; }

        /// <summary>
        /// Byte offset where the frame can be decoded from: for video the PAT written before it (so a range starting
        /// there is playable on its own), for audio its first packet.
        /// </summary>
        public long Offset { get; }

        public byte[] Data { get; }
        public bool IsKeyFrame { get; }
        public AudioFormat Audio { get; }
        public bool IsAudio => Audio != null;

        /// <summary>The raw AAC access units in the ADTS data (usually one).</summary>
        public IEnumerable<byte[]> AacAccessUnits()
        {
            for (var offset = 0; offset + 7 <= Data.Length;)
            {
                var headerLength = (Data[offset + 1] & 0x01) == 0 ? 9 : 7;
                var length = ((Data[offset + 3] & 0x03) << 11) | (Data[offset + 4] << 3) | (Data[offset + 5] >> 5);

                if (length < headerLength || offset + length > Data.Length)
                {
                    yield break;
                }

                yield return Data.AsSpan(offset + headerLength, length - headerLength).ToArray();
                offset += length;
            }
        }
    }
}
