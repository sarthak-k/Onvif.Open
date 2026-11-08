// Sarthak Killedar
// 2026-11-08

using System;
using System.Collections.Generic;

namespace Onvif.Open.Benchmarks
{
    /// <summary>
    /// A synthetic 1080p H.264 stream: one second at 25 fps with a key frame (150 KB) and 24 P frames (15 KB),
    /// sent as RTP the way cameras do (FU-A fragments of 1400 bytes; SPS and PPS before the key frame).
    /// </summary>
    internal static class Media
    {
        public const int Mtu = 1400;
        public const int KeyFrameSize = 150_000;
        public const int FrameSize = 15_000;
        public const int Fps = 25;

        public static readonly byte[] Sps = { 0x67, 0x42, 0x00, 0x1F, 0xE9, 0x01, 0x40, 0x7B, 0x20 };
        public static readonly byte[] Pps = { 0x68, 0xCE, 0x38, 0x80 };

        /// <summary>RTP packets (12 byte header + payload) for one GOP.</summary>
        public static List<byte[]> RtpGop()
        {
            var packets = new List<byte[]>();
            ushort sequence = 0;
            var random = new Random(7);

            for (var frame = 0; frame < Fps; frame++)
            {
                var timestamp = (uint)(frame * 3600);
                var key = frame == 0;

                if (key)
                {
                    packets.Add(Rtp(sequence++, timestamp, false, Sps));
                    packets.Add(Rtp(sequence++, timestamp, false, Pps));
                }

                var nal = new byte[key ? KeyFrameSize : FrameSize];
                random.NextBytes(nal);
                nal[0] = (byte)(key ? 0x65 : 0x41);

                // FU-A: indicator (F, NRI, type 28) + header (S, E, NAL type) + fragment.
                var offset = 1;
                while (offset < nal.Length)
                {
                    var take = Math.Min(Mtu, nal.Length - offset);
                    var payload = new byte[2 + take];
                    payload[0] = (byte)((nal[0] & 0xE0) | 28);
                    payload[1] = (byte)((offset == 1 ? 0x80 : 0) | (offset + take == nal.Length ? 0x40 : 0) | (nal[0] & 0x1F));
                    Array.Copy(nal, offset, payload, 2, take);
                    offset += take;
                    packets.Add(Rtp(sequence++, timestamp, offset == nal.Length, payload));
                }
            }

            return packets;
        }

        private static byte[] Rtp(ushort sequence, uint timestamp, bool marker, byte[] payload)
        {
            var packet = new byte[12 + payload.Length];
            packet[0] = 0x80;
            packet[1] = (byte)((marker ? 0x80 : 0) | 96);
            packet[2] = (byte)(sequence >> 8);
            packet[3] = (byte)sequence;
            packet[4] = (byte)(timestamp >> 24);
            packet[5] = (byte)(timestamp >> 16);
            packet[6] = (byte)(timestamp >> 8);
            packet[7] = (byte)timestamp;
            payload.CopyTo(packet, 12);
            return packet;
        }
    }
}
