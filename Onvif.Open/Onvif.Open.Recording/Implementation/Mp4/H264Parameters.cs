// Sarthak Killedar
// 2026-11-10

using System;
using System.Collections.Generic;

namespace Onvif.Open.Recording.Implementation.Mp4
{
    /// <summary>NAL units of an Annex-B access unit (start codes removed).</summary>
    public static class AnnexB
    {
        public static List<ReadOnlyMemory<byte>> Split(ReadOnlyMemory<byte> accessUnit)
        {
            var units = new List<ReadOnlyMemory<byte>>();
            var data = accessUnit.Span;
            var start = -1;
            var i = 0;

            while (i + 2 < data.Length)
            {
                if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1)
                {
                    if (start >= 0)
                    {
                        // A 4-byte start code leaves a zero before this one.
                        var end = i > start && data[i - 1] == 0 ? i - 1 : i;
                        if (end > start)
                        {
                            units.Add(accessUnit.Slice(start, end - start));
                        }
                    }

                    i += 3;
                    start = i;
                }
                else
                {
                    i++;
                }
            }

            if (start >= 0 && start < data.Length)
            {
                units.Add(accessUnit.Slice(start));
            }

            return units;
        }
    }

    /// <summary>What a decoder needs from an H.264 sequence parameter set (ITU-T H.264 7.3.2.1.1).</summary>
    public sealed record H264Sps(byte Profile, byte Compatibility, byte Level, int Width, int Height)
    {
        /// <summary>The RFC 6381 codec string, e.g. "avc1.64001f".</summary>
        public string CodecString => $"avc1.{Profile:x2}{Compatibility:x2}{Level:x2}";

        /// <summary>Reads an SPS NAL unit (with its header byte); null when it is not one or is cut short.</summary>
        public static H264Sps Parse(ReadOnlySpan<byte> nal)
        {
            if (nal.Length < 4 || (nal[0] & 0x1F) != 7)
            {
                return null;
            }

            try
            {
                var bits = new BitReader(Unescape(nal.Slice(1)));
                var profile = (byte)bits.Read(8);
                var compatibility = (byte)bits.Read(8);
                var level = (byte)bits.Read(8);
                bits.ReadUe();

                var chroma = 1;
                if (profile is 100 or 110 or 122 or 244 or 44 or 83 or 86 or 118 or 128 or 138 or 139 or 134 or 135)
                {
                    chroma = (int)bits.ReadUe();
                    if (chroma == 3)
                    {
                        bits.Read(1);
                    }

                    bits.ReadUe();
                    bits.ReadUe();
                    bits.Read(1);
                    if (bits.Read(1) == 1)
                    {
                        for (var i = 0; i < (chroma != 3 ? 8 : 12); i++)
                        {
                            if (bits.Read(1) == 1)
                            {
                                SkipScalingList(ref bits, i < 6 ? 16 : 64);
                            }
                        }
                    }
                }

                bits.ReadUe();
                var pocType = bits.ReadUe();
                if (pocType == 0)
                {
                    bits.ReadUe();
                }
                else if (pocType == 1)
                {
                    bits.Read(1);
                    bits.ReadSe();
                    bits.ReadSe();
                    var cycle = bits.ReadUe();
                    for (var i = 0; i < cycle; i++)
                    {
                        bits.ReadSe();
                    }
                }

                bits.ReadUe();
                bits.Read(1);
                var widthInMbs = (int)bits.ReadUe() + 1;
                var heightInMapUnits = (int)bits.ReadUe() + 1;
                var frameMbsOnly = (int)bits.Read(1);
                if (frameMbsOnly == 0)
                {
                    bits.Read(1);
                }

                bits.Read(1);
                int left = 0, right = 0, top = 0, bottom = 0;
                if (bits.Read(1) == 1)
                {
                    left = (int)bits.ReadUe();
                    right = (int)bits.ReadUe();
                    top = (int)bits.ReadUe();
                    bottom = (int)bits.ReadUe();
                }

                var cropX = chroma is 1 or 2 ? 2 : 1;
                var cropY = (chroma == 1 ? 2 : 1) * (2 - frameMbsOnly);
                var width = widthInMbs * 16 - cropX * (left + right);
                var height = (2 - frameMbsOnly) * heightInMapUnits * 16 - cropY * (top + bottom);
                return width > 0 && height > 0 ? new H264Sps(profile, compatibility, level, width, height) : null;
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static void SkipScalingList(ref BitReader bits, int size)
        {
            int last = 8, next = 8;
            for (var j = 0; j < size; j++)
            {
                if (next != 0)
                {
                    next = (last + bits.ReadSe() + 256) % 256;
                }

                last = next == 0 ? last : next;
            }
        }

        /// <summary>The RBSP: emulation prevention bytes (00 00 03) removed.</summary>
        private static byte[] Unescape(ReadOnlySpan<byte> data)
        {
            var output = new byte[data.Length];
            var length = 0;
            var zeros = 0;
            foreach (var b in data)
            {
                if (zeros >= 2 && b == 3)
                {
                    zeros = 0;
                    continue;
                }

                output[length++] = b;
                zeros = b == 0 ? zeros + 1 : 0;
            }

            return output.AsSpan(0, length).ToArray();
        }

        private ref struct BitReader
        {
            public BitReader(byte[] data)
            {
                _data = data;
                _position = 0;
            }

            private readonly byte[] _data;
            private int _position;

            public uint Read(int count)
            {
                uint value = 0;
                for (var i = 0; i < count; i++, _position++)
                {
                    value = (value << 1) | (uint)((_data[_position >> 3] >> (7 - (_position & 7))) & 1);
                }

                return value;
            }

            public uint ReadUe()
            {
                var zeros = 0;
                while (Read(1) == 0)
                {
                    if (++zeros > 31)
                    {
                        throw new IndexOutOfRangeException();
                    }
                }

                return zeros == 0 ? 0 : (1u << zeros) - 1 + Read(zeros);
            }

            public int ReadSe()
            {
                var value = ReadUe();
                return (value & 1) == 1 ? (int)((value + 1) / 2) : -(int)(value / 2);
            }
        }
    }
}
