// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Rtp;

namespace Onvif.Open.Streaming.Implementation.Depacketizer
{
    /// <summary>
    /// Template for NAL unit based payload formats (H.264 RFC 6184, H.265 RFC 7798).
    /// Collects the NAL units of an access unit and emits it in Annex-B format, injecting the
    /// out-of-band parameter sets (from SDP) in front of key frames that do not carry them.
    /// </summary>
    /// <remarks>
    /// The access unit is assembled in place in one reused buffer (start codes, NAL units and FU fragments are
    /// appended as they arrive), so a frame costs one allocation of its own size and one copy.
    /// </remarks>
    internal abstract class AnnexBDepacketizer : IRtpDepacketizer
    {
        #region Constructor

        protected AnnexBDepacketizer(int clockRate, IEnumerable<byte[]> parameterSets)
        {
            _clock = new RtpTimestampClock(clockRate);

            foreach (var parameterSet in parameterSets ?? Array.Empty<byte[]>())
            {
                if (parameterSet != null && parameterSet.Length > 0)
                {
                    _parameterSets[GetNalType(parameterSet) & MaxNalType] = parameterSet;
                }
            }
        }

        #endregion

        #region Private Variables

        // NAL unit types are 5 bits (H.264) or 6 bits (H.265): a 64 bit mask holds the types present.
        private const int MaxNalType = 63;
        private const int StartCodeLength = 4;
        private const int InitialCapacity = 64 * 1024;

        private readonly RtpTimestampClock _clock;

        /// <summary>The latest parameter set of each type, in type order (as decoders expect: VPS, SPS, PPS).</summary>
        private readonly byte[][] _parameterSets = new byte[MaxNalType + 1][];

        private byte[] _buffer = new byte[InitialCapacity];
        private int _length;
        private bool _isKeyFrame;
        private ulong _parameterSetsPresent;

        /// <summary>Where the fragmented NAL unit being assembled starts in the buffer; -1: none.</summary>
        private int _fragmentStart = -1;

        private bool _hasTimestamp;
        private uint _timestamp;
        private bool _hasSequence;
        private ushort _expectedSequence;

        #endregion

        #region Public Variables

        public abstract VideoCodec Codec { get; }

        #endregion

        #region Public Methods

        public void Process(RtpPacket packet, ICollection<VideoFrame> frames)
        {
            if (_hasSequence && packet.SequenceNumber != _expectedSequence)
            {
                // Packet loss: whatever fragment was being assembled is unusable.
                DropFragment();
            }

            _hasSequence = true;
            _expectedSequence = unchecked((ushort)(packet.SequenceNumber + 1));

            if (_hasTimestamp && packet.Timestamp != _timestamp)
            {
                // Some cameras never set the marker bit: a new timestamp also starts a new access unit.
                Flush(frames);
            }

            _hasTimestamp = true;
            _timestamp = packet.Timestamp;

            ProcessPayload(packet.Payload.Span);

            if (packet.Marker)
            {
                Flush(frames);
            }
        }

        #endregion

        #region Protected Methods

        protected abstract void ProcessPayload(ReadOnlySpan<byte> payload);

        protected abstract int GetNalType(ReadOnlySpan<byte> nalUnit);

        protected abstract bool IsKeyFrame(int nalType);

        protected abstract bool IsParameterSet(int nalType);

        protected void AddNalUnit(ReadOnlySpan<byte> nalUnit)
        {
            if (nalUnit.IsEmpty)
            {
                return;
            }

            var start = _length;
            AppendStartCode();
            Append(nalUnit);
            Classify(start + StartCodeLength);
        }

        protected void StartFragment(ReadOnlySpan<byte> reconstructedHeader, ReadOnlySpan<byte> data)
        {
            DropFragment();
            _fragmentStart = _length;
            AppendStartCode();
            Append(reconstructedHeader);
            Append(data);
        }

        protected void ContinueFragment(ReadOnlySpan<byte> data, bool isLast)
        {
            if (_fragmentStart < 0)
            {
                // Start fragment was lost.
                return;
            }

            Append(data);

            if (isLast)
            {
                var nalStart = _fragmentStart + StartCodeLength;
                _fragmentStart = -1;
                Classify(nalStart);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>Notes the type of the NAL unit just completed at <paramref name="nalStart"/>.</summary>
        private void Classify(int nalStart)
        {
            var nalUnit = _buffer.AsSpan(nalStart, _length - nalStart);
            var type = GetNalType(nalUnit) & MaxNalType;

            _isKeyFrame |= IsKeyFrame(type);

            if (IsParameterSet(type))
            {
                // Keep the latest in-band parameter sets for later key frames.
                _parameterSets[type] = nalUnit.ToArray();
                _parameterSetsPresent |= 1UL << type;
            }
        }

        private void DropFragment()
        {
            if (_fragmentStart >= 0)
            {
                _length = _fragmentStart;
                _fragmentStart = -1;
            }
        }

        private void Flush(ICollection<VideoFrame> frames)
        {
            DropFragment();

            if (_length == 0)
            {
                return;
            }

            // Key frames get the parameter sets they do not carry in front, so every segment can start with them.
            var prefix = 0;
            if (_isKeyFrame)
            {
                for (var type = 0; type <= MaxNalType; type++)
                {
                    if (_parameterSets[type] != null && (_parameterSetsPresent & (1UL << type)) == 0)
                    {
                        prefix += StartCodeLength + _parameterSets[type].Length;
                    }
                }
            }

            // Every byte is written below, so the array need not be zeroed first.
            var data = GC.AllocateUninitializedArray<byte>(prefix + _length);
            var output = data.AsSpan();

            if (prefix > 0)
            {
                for (var type = 0; type <= MaxNalType; type++)
                {
                    if (_parameterSets[type] != null && (_parameterSetsPresent & (1UL << type)) == 0)
                    {
                        WriteStartCode(output);
                        _parameterSets[type].CopyTo(output.Slice(StartCodeLength));
                        output = output.Slice(StartCodeLength + _parameterSets[type].Length);
                    }
                }
            }

            _buffer.AsSpan(0, _length).CopyTo(output);

            // A damaged non key frame is still delivered: decoders conceal the error until the next key frame.
            frames.Add(new VideoFrame(Codec, data, _isKeyFrame, _timestamp, _clock.ToTime(_timestamp)));

            _length = 0;
            _isKeyFrame = false;
            _parameterSetsPresent = 0;
        }

        private void AppendStartCode()
        {
            EnsureCapacity(StartCodeLength);
            WriteStartCode(_buffer.AsSpan(_length));
            _length += StartCodeLength;
        }

        private void Append(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(data.Length);
            data.CopyTo(_buffer.AsSpan(_length));
            _length += data.Length;
        }

        private void EnsureCapacity(int additional)
        {
            if (_length + additional > _buffer.Length)
            {
                var grown = new byte[Math.Max(_buffer.Length * 2, _length + additional)];
                _buffer.AsSpan(0, _length).CopyTo(grown);
                _buffer = grown;
            }
        }

        private static void WriteStartCode(Span<byte> destination)
        {
            destination[0] = 0;
            destination[1] = 0;
            destination[2] = 0;
            destination[3] = 1;
        }

        #endregion
    }
}
