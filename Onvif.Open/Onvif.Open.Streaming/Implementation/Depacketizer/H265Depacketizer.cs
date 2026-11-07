// Sarthak Killedar
// 2026-11-07

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Streaming.Implementation.Depacketizer
{
    /// <summary>
    /// H.265 / HEVC RTP payload format (RFC 7798): single NAL unit, aggregation (AP) and fragmentation (FU) packets.
    /// </summary>
    internal sealed class H265Depacketizer : AnnexBDepacketizer
    {
        #region Constructor

        public H265Depacketizer(int clockRate, IEnumerable<byte[]> parameterSets)
            : base(clockRate, parameterSets)
        {

        }

        #endregion

        #region Private Variables

        private const int AggregationPacket = 48;
        private const int FragmentationUnit = 49;
        private const int Vps = 32;
        private const int Sps = 33;
        private const int Pps = 34;

        #endregion

        #region Public Variables

        public override VideoCodec Codec => VideoCodec.H265;

        #endregion

        #region Protected Methods

        protected override void ProcessPayload(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 2)
            {
                return;
            }

            var type = GetNalType(payload);

            switch (type)
            {
                case AggregationPacket:
                    ProcessAggregation(payload.Slice(2));
                    break;
                case FragmentationUnit:
                    ProcessFragment(payload);
                    break;
                default:
                    if (type < AggregationPacket)
                    {
                        AddNalUnit(payload);
                    }
                    break;
            }
        }

        protected override int GetNalType(ReadOnlySpan<byte> nalUnit)
        {
            return nalUnit.IsEmpty ? 0 : (nalUnit[0] >> 1) & 0x3F;
        }

        protected override bool IsKeyFrame(int nalType)
        {
            // IRAP pictures: BLA, IDR and CRA.
            return nalType >= 16 && nalType <= 21;
        }

        protected override bool IsParameterSet(int nalType)
        {
            return nalType == Vps || nalType == Sps || nalType == Pps;
        }

        #endregion

        #region Private Methods

        private void ProcessAggregation(ReadOnlySpan<byte> data)
        {
            while (data.Length > 2)
            {
                var size = BinaryPrimitives.ReadUInt16BigEndian(data);
                data = data.Slice(2);

                if (size == 0 || size > data.Length)
                {
                    return;
                }

                AddNalUnit(data.Slice(0, size));
                data = data.Slice(size);
            }
        }

        private void ProcessFragment(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 3)
            {
                return;
            }

            var fuHeader = payload[2];
            var isStart = (fuHeader & 0x80) != 0;
            var isEnd = (fuHeader & 0x40) != 0;
            var data = payload.Slice(3);

            if (isStart)
            {
                Span<byte> nalHeader = stackalloc byte[]
                {
                    (byte)((payload[0] & 0x81) | ((fuHeader & 0x3F) << 1)),
                    payload[1]
                };
                StartFragment(nalHeader, data);

                if (isEnd)
                {
                    ContinueFragment(ReadOnlySpan<byte>.Empty, true);
                }
            }
            else
            {
                ContinueFragment(data, isEnd);
            }
        }

        #endregion
    }
}
