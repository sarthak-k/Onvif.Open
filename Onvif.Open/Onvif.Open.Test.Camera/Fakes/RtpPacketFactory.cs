// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;

namespace Onvif.Open.Test.Camera.Fakes
{
    internal static class RtpPacketFactory
    {
        public static byte[] Create(ushort sequence, uint timestamp, bool marker, byte[] payload, byte payloadType = 96)
        {
            var packet = new byte[12 + payload.Length];
            packet[0] = 0x80;
            packet[1] = (byte)((marker ? 0x80 : 0) | payloadType);
            packet[2] = (byte)(sequence >> 8);
            packet[3] = (byte)sequence;
            packet[4] = (byte)(timestamp >> 24);
            packet[5] = (byte)(timestamp >> 16);
            packet[6] = (byte)(timestamp >> 8);
            packet[7] = (byte)timestamp;
            packet[8] = 0x12;
            packet[9] = 0x34;
            packet[10] = 0x56;
            packet[11] = 0x78;
            Buffer.BlockCopy(payload, 0, packet, 12, payload.Length);
            return packet;
        }

        /// <summary>An RTP packet with the ONVIF replay header extension (0xABAC) carrying <paramref name="captureTime"/>.</summary>
        public static byte[] CreateReplay(ushort sequence, uint timestamp, bool marker, byte[] payload, DateTimeOffset captureTime, bool cleanPoint = false)
        {
            var plain = Create(sequence, timestamp, marker, payload);
            var packet = new byte[plain.Length + 16];
            Buffer.BlockCopy(plain, 0, packet, 0, 12);
            packet[0] |= 0x10;

            // Extension header: profile 0xABAC, length 3 words.
            packet[12] = 0xAB;
            packet[13] = 0xAC;
            packet[15] = 3;

            var elapsed = captureTime.UtcDateTime - new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var seconds = (uint)Math.Floor(elapsed.TotalSeconds);
            var fraction = (uint)((elapsed.Ticks % TimeSpan.TicksPerSecond) * 4294967296.0 / TimeSpan.TicksPerSecond);
            WriteUInt32(packet, 16, seconds);
            WriteUInt32(packet, 20, fraction);
            packet[24] = (byte)(cleanPoint ? 0x80 : 0);

            Buffer.BlockCopy(plain, 12, packet, 28, payload.Length);
            return packet;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        /// <summary>Splits an H.264 NAL unit into FU-A payloads.</summary>
        public static List<byte[]> FragmentH264(byte[] nal, int chunkSize)
        {
            var result = new List<byte[]>();
            var indicator = (byte)((nal[0] & 0xE0) | 28);
            var type = (byte)(nal[0] & 0x1F);
            var body = nal.Skip(1).ToArray();

            for (var offset = 0; offset < body.Length; offset += chunkSize)
            {
                var chunk = body.Skip(offset).Take(chunkSize).ToArray();
                var header = type;
                if (offset == 0)
                {
                    header |= 0x80;
                }

                if (offset + chunkSize >= body.Length)
                {
                    header |= 0x40;
                }

                result.Add(new[] { indicator, header }.Concat(chunk).ToArray());
            }

            return result;
        }
    }
}
