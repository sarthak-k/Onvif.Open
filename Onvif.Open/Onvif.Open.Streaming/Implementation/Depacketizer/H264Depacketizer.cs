// Sarthak Killedar
// 2026-11-07

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Onvif.Open.Core.Streaming;

namespace Onvif.Open.Streaming.Implementation.Depacketizer
{
    /// <summary>
    /// H.264 RTP payload format (RFC 6184): single NAL unit, STAP-A and FU-A packets.
    /// </summary>
    internal sealed class H264Depacketizer : AnnexBDepacketizer
    {
        #region Constructor

        public H264Depacketizer(int clockRate, IEnumerable<byte[]> parameterSets)
            : base(clockRate, parameterSets)
        {

        }

        #endregion

        #region Private Variables

        private const int StapA = 24;
        private const int FuA = 28;
        private const int Idr = 5;
        private const int Sps = 7;
        private const int Pps = 8;

        #endregion

        #region Public Variables

        public override VideoCodec Codec => VideoCodec.H264;

        #endregion

        #region Protected Methods

        protected override void ProcessPayload(ReadOnlySpan<byte> payload)
        {
            if (payload.IsEmpty)
            {
                return;
            }

            var type = payload[0] & 0x1F;

            switch (type)
            {
                case StapA:
                    ProcessAggregation(payload.Slice(1));
                    break;
                case FuA:
                    ProcessFragment(payload);
                    break;
                default:
                    if (type >= 1 && type <= 23)
                    {
                        AddNalUnit(payload);
                    }
                    break;
            }
        }

        protected override int GetNalType(ReadOnlySpan<byte> nalUnit)
        {
            return nalUnit.IsEmpty ? 0 : nalUnit[0] & 0x1F;
        }

        protected override bool IsKeyFrame(int nalType)
        {
            return nalType == Idr;
        }

        protected override bool IsParameterSet(int nalType)
        {
            return nalType == Sps || nalType == Pps;
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
            if (payload.Length < 2)
            {
                return;
            }

            var indicator = payload[0];
            var header = payload[1];
            var isStart = (header & 0x80) != 0;
            var isEnd = (header & 0x40) != 0;
            var data = payload.Slice(2);

            if (isStart)
            {
                Span<byte> nalHeader = stackalloc byte[] { (byte)((indicator & 0xE0) | (header & 0x1F)) };
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
