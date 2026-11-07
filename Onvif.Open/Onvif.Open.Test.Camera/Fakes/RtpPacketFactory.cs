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
