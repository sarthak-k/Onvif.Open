// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.IO;
using Onvif.Open.Core.Streaming;
using Onvif.Open.Streaming.Implementation.Rtp;

namespace Onvif.Open.Streaming.Implementation.Depacketizer
{
    /// <summary>
    /// Template for NAL unit based payload formats (H.264 RFC 6184, H.265 RFC 7798).
    /// Collects the NAL units of an access unit and emits it in Annex-B format, injecting the
    /// out-of-band parameter sets (from SDP) in front of key frames that do not carry them.
    /// </summary>
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
                    _parameterSets[GetNalType(parameterSet)] = parameterSet;
                }
            }
        }

        #endregion

        #region Private Variables

        private static readonly byte[] StartCode = { 0, 0, 0, 1 };

        private readonly RtpTimestampClock _clock;
        private readonly SortedDictionary<int, byte[]> _parameterSets = new SortedDictionary<int, byte[]>();
        private readonly List<byte[]> _nalUnits = new List<byte[]>();
        private MemoryStream _fragment;
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
                _fragment = null;
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

        /// <summary>Parses one RTP payload; implementations call <see cref="AddNalUnit"/> / the fragment helpers.</summary>
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

            var copy = nalUnit.ToArray();
            var type = GetNalType(copy);

            if (IsParameterSet(type))
            {
                // Keep the latest in-band parameter sets for later key frames.
                _parameterSets[type] = copy;
            }

            _nalUnits.Add(copy);
        }

        protected void StartFragment(ReadOnlySpan<byte> reconstructedHeader, ReadOnlySpan<byte> data)
        {
            _fragment = new MemoryStream();
            _fragment.Write(reconstructedHeader);
            _fragment.Write(data);
        }

        protected void ContinueFragment(ReadOnlySpan<byte> data, bool isLast)
        {
            if (_fragment == null)
            {
                // Start fragment was lost.
                return;
            }

            _fragment.Write(data);

            if (isLast)
            {
                AddNalUnit(_fragment.ToArray());
                _fragment = null;
            }
        }

        #endregion

        #region Private Methods

        private void Flush(ICollection<VideoFrame> frames)
        {
            _fragment = null;

            if (_nalUnits.Count == 0)
            {
                return;
            }

            var isKeyFrame = false;
            var parameterSetsPresent = new HashSet<int>();

            foreach (var nal in _nalUnits)
            {
                var type = GetNalType(nal);
                isKeyFrame |= IsKeyFrame(type);
                if (IsParameterSet(type))
                {
                    parameterSetsPresent.Add(type);
                }
            }

            using (var output = new MemoryStream())
            {
                if (isKeyFrame)
                {
                    foreach (var parameterSet in _parameterSets)
                    {
                        if (!parameterSetsPresent.Contains(parameterSet.Key))
                        {
                            Write(output, parameterSet.Value);
                        }
                    }
                }

                foreach (var nal in _nalUnits)
                {
                    Write(output, nal);
                }

                // A damaged non key frame is still delivered: decoders conceal the error until the next key frame.
                frames.Add(new VideoFrame(Codec, output.ToArray(), isKeyFrame, _timestamp, _clock.ToTime(_timestamp)));
            }

            _nalUnits.Clear();
        }

        private static void Write(Stream output, byte[] nalUnit)
        {
            output.Write(StartCode, 0, StartCode.Length);
            output.Write(nalUnit, 0, nalUnit.Length);
        }

        #endregion
    }
}
