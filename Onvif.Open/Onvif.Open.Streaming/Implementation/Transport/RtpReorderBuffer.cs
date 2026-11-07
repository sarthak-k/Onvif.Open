// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using Onvif.Open.Streaming.Implementation.Rtp;

namespace Onvif.Open.Streaming.Implementation.Transport
{
    /// <summary>
    /// Puts RTP packets received over UDP back in sequence order. Packets are held until the
    /// missing one arrives or more than <c>capacity</c> packets are waiting, in which case the gap
    /// is treated as loss. Late duplicates are discarded. The first few packets are held until the
    /// starting sequence number is known, so re-ordering at the very start is handled too.
    /// </summary>
    internal sealed class RtpReorderBuffer
    {
        #region Constructor

        public RtpReorderBuffer(int capacity = 64)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _capacity = capacity;
            _startupDepth = Math.Min(DefaultStartupDepth, capacity);
        }

        #endregion

        #region Private Variables

        private const int DefaultStartupDepth = 4;

        private readonly int _capacity;
        private readonly int _startupDepth;
        private readonly Dictionary<ushort, byte[]> _pending = new Dictionary<ushort, byte[]>();
        private bool _started;
        private ushort _expected;

        #endregion

        #region Public Methods

        /// <summary>Adds a packet and appends every packet that is now in order to <paramref name="ready"/>.</summary>
        public void Add(byte[] data, ICollection<byte[]> ready)
        {
            if (!RtpPacket.TryParse(data, out var packet))
            {
                return;
            }

            var sequence = packet.SequenceNumber;

            if (!_started)
            {
                _pending[sequence] = data;

                if (_pending.Count >= _startupDepth)
                {
                    _started = true;
                    _expected = OldestPending(sequence);
                    Drain(ready);
                }

                return;
            }

            // Signed 16 bit distance copes with wrap-around; negative means already delivered or skipped.
            if (unchecked((short)(sequence - _expected)) < 0)
            {
                return;
            }

            _pending[sequence] = data;
            Drain(ready);

            if (_pending.Count > _capacity)
            {
                // Give up waiting for the gap: continue from the oldest pending packet.
                _expected = OldestPending(_expected);
                Drain(ready);
            }
        }

        #endregion

        #region Private Methods

        private void Drain(ICollection<byte[]> ready)
        {
            while (_pending.Remove(_expected, out var data))
            {
                ready.Add(data);
                _expected = unchecked((ushort)(_expected + 1));
            }
        }

        private ushort OldestPending(ushort reference)
        {
            var oldest = reference;
            var smallest = int.MaxValue;

            foreach (var sequence in _pending.Keys)
            {
                // Signed distance from a recent sequence number orders packets correctly across wrap-around.
                int distance = unchecked((short)(sequence - reference));
                if (distance < smallest)
                {
                    smallest = distance;
                    oldest = sequence;
                }
            }

            return oldest;
        }

        #endregion
    }
}
