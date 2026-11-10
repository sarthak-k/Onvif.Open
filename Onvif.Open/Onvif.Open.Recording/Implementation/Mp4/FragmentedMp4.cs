// Sarthak Killedar
// 2026-11-10

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Onvif.Open.Recording.Implementation.Mp4
{
    /// <summary>
    /// Fragmented MP4 (ISO/IEC 14496-12, the format of Media Source Extensions) for one H.264 video track: an init
    /// segment with the decoder configuration, then one fragment per frame, so a browser shows a frame as soon as it
    /// arrives. Timestamps are in 90 kHz units.
    /// </summary>
    public static class FragmentedMp4
    {
        #region Public Variables

        public const uint Timescale = 90_000;
        private const uint TrackId = 1;

        #endregion

        #region Public Methods

        /// <summary>The init segment (ftyp + moov) for an H.264 stream with these parameter sets.</summary>
        public static byte[] InitSegment(H264Sps sps, ReadOnlySpan<byte> spsNal, ReadOnlySpan<byte> ppsNal)
        {
            var output = new Writer();
            output.Box("ftyp", b => b.Ascii("isom").U32(0x200).Ascii("isom").Ascii("iso6").Ascii("avc1").Ascii("mp41"));

            var avcC = new Writer()
                .U8(1).U8(sps.Profile).U8(sps.Compatibility).U8(sps.Level)
                .U8(0xFF) // 4-byte NAL lengths
                .U8(0xE1).U16((ushort)spsNal.Length).Bytes(spsNal)
                .U8(1).U16((ushort)ppsNal.Length).Bytes(ppsNal)
                .ToArray();

            output.Box("moov", moov => moov
                .FullBox("mvhd", 0, 0, b => b.U32(0).U32(0).U32(1000).U32(0).U32(0x00010000).U16(0x0100).Zeros(10).Matrix().Zeros(24).U32(TrackId + 1))
                .Box("trak", trak => trak
                    .FullBox("tkhd", 0, 3, b => b.U32(0).U32(0).U32(TrackId).U32(0).U32(0).Zeros(8).U16(0).U16(0).U16(0).U16(0).Matrix().U32((uint)sps.Width << 16).U32((uint)sps.Height << 16))
                    .Box("mdia", mdia => mdia
                        .FullBox("mdhd", 0, 0, b => b.U32(0).U32(0).U32(Timescale).U32(0).U16(0x55C4).U16(0))
                        .FullBox("hdlr", 0, 0, b => b.U32(0).Ascii("vide").Zeros(12).Ascii("VideoHandler").U8(0))
                        .Box("minf", minf => minf
                            .FullBox("vmhd", 0, 1, b => b.U16(0).U16(0).U16(0).U16(0))
                            .Box("dinf", dinf => dinf.FullBox("dref", 0, 0, b => b.U32(1).FullBox("url ", 0, 1, _ => { })))
                            .Box("stbl", stbl => stbl
                                .FullBox("stsd", 0, 0, b => b.U32(1).Box("avc1", avc1 => avc1
                                    .Zeros(6).U16(1).U16(0).U16(0).Zeros(12)
                                    .U16((ushort)sps.Width).U16((ushort)sps.Height)
                                    .U32(0x00480000).U32(0x00480000).U32(0).U16(1).Zeros(32).U16(0x0018).U16(0xFFFF)
                                    .Box("avcC", c => c.Bytes(avcC))))
                                .FullBox("stts", 0, 0, b => b.U32(0))
                                .FullBox("stsc", 0, 0, b => b.U32(0))
                                .FullBox("stsz", 0, 0, b => b.U32(0).U32(0))
                                .FullBox("stco", 0, 0, b => b.U32(0))))))
                .Box("mvex", mvex => mvex.FullBox("trex", 0, 0, b => b.U32(TrackId).U32(1).U32(0).U32(0).U32(0))));

            return output.ToArray();
        }

        /// <summary>
        /// One frame as a fragment (moof + mdat). <paramref name="accessUnit"/> is Annex-B; parameter sets and access
        /// unit delimiters are left out (they are in the init segment).
        /// </summary>
        public static byte[] Fragment(uint sequence, ulong decodeTime, uint duration, ReadOnlyMemory<byte> accessUnit, bool isKeyFrame)
        {
            var sample = new Writer();
            foreach (var nal in AnnexB.Split(accessUnit))
            {
                var type = nal.Span[0] & 0x1F;
                if (type is 7 or 8 or 9)
                {
                    continue;
                }

                sample.U32((uint)nal.Length).Bytes(nal.Span);
            }

            var data = sample.ToArray();

            // The sample's offset counts from the start of the moof: write it once to measure it.
            byte[] Moof(uint dataOffset) => new Writer().Box("moof", moof => moof
                .FullBox("mfhd", 0, 0, b => b.U32(sequence))
                .Box("traf", traf => traf
                    .FullBox("tfhd", 0, 0x020000, b => b.U32(TrackId))
                    .FullBox("tfdt", 1, 0, b => b.U64(decodeTime))
                    .FullBox("trun", 0, 0x000701, b => b
                        .U32(1)
                        .U32(dataOffset)
                        .U32(duration)
                        .U32((uint)data.Length)
                        .U32(isKeyFrame ? 0x02000000u : 0x01010000u)))).ToArray();

            var output = new Writer();
            output.Bytes(Moof((uint)Moof(0).Length + 8));
            output.Box("mdat", b => b.Bytes(data));
            return output.ToArray();
        }

        #endregion

        /// <summary>Big-endian box writer.</summary>
        private sealed class Writer
        {
            private readonly MemoryStream _stream = new MemoryStream();

            public byte[] ToArray() => _stream.ToArray();

            public Writer U8(byte value)
            {
                _stream.WriteByte(value);
                return this;
            }

            public Writer U16(ushort value)
            {
                Span<byte> b = stackalloc byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(b, value);
                _stream.Write(b);
                return this;
            }

            public Writer U32(uint value)
            {
                Span<byte> b = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(b, value);
                _stream.Write(b);
                return this;
            }

            public Writer U64(ulong value)
            {
                Span<byte> b = stackalloc byte[8];
                BinaryPrimitives.WriteUInt64BigEndian(b, value);
                _stream.Write(b);
                return this;
            }

            public Writer Bytes(ReadOnlySpan<byte> value)
            {
                _stream.Write(value);
                return this;
            }

            public Writer Ascii(string value) => Bytes(Encoding.ASCII.GetBytes(value));

            public Writer Zeros(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    _stream.WriteByte(0);
                }

                return this;
            }

            /// <summary>The identity transformation matrix.</summary>
            public Writer Matrix() => U32(0x00010000).U32(0).U32(0).U32(0).U32(0x00010000).U32(0).U32(0).U32(0).U32(0x40000000);

            public Writer Box(string type, Action<Writer> content)
            {
                var body = new Writer();
                content(body);
                var bytes = body.ToArray();
                return U32((uint)(8 + bytes.Length)).Ascii(type).Bytes(bytes);
            }

            public Writer FullBox(string type, byte version, uint flags, Action<Writer> content) =>
                Box(type, b =>
                {
                    b.U32(((uint)version << 24) | (flags & 0xFFFFFF));
                    content(b);
                });
        }
    }
}
