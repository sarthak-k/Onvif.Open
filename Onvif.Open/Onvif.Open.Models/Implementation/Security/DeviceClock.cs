// Sarthak Killedar
// 2026-11-07

using System;
using System.Runtime.CompilerServices;
using Onvif.Open.Core.Abstract.Interface.Camera;

namespace Onvif.Open.Core.Implementation.Security
{
    /// <summary>
    /// The difference between a device's clock and this machine's clock. WS-Security UsernameTokens
    /// carry a creation time that devices reject when it is too far from their own clock, so tokens are
    /// stamped in device time: <c>UtcNow = DateTime.UtcNow + Offset</c>.
    /// </summary>
    public sealed class DeviceClock
    {
        #region Constructor

        private DeviceClock()
        {

        }

        #endregion

        #region Private Variables

        private static readonly ConditionalWeakTable<ICameraInformation, DeviceClock> Clocks = new ConditionalWeakTable<ICameraInformation, DeviceClock>();

        private readonly System.Threading.Lock _sync = new System.Threading.Lock();
        private TimeSpan _offset;
        private DateTimeOffset? _lastSynchronized;

        #endregion

        #region Public Variables

        /// <summary>Device time minus local time.</summary>
        public TimeSpan Offset
        {
            get
            {
                lock (_sync)
                {
                    return _offset;
                }
            }
        }

        /// <summary>When the offset was last measured; null when it never was.</summary>
        public DateTimeOffset? LastSynchronized
        {
            get
            {
                lock (_sync)
                {
                    return _lastSynchronized;
                }
            }
        }

        /// <summary>The current time on the device.</summary>
        public DateTime UtcNow => DateTime.UtcNow + Offset;

        #endregion

        #region Public Methods

        /// <summary>The clock of <paramref name="camera"/> (one per camera instance, created on first use).</summary>
        public static DeviceClock For(ICameraInformation camera)
        {
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            return Clocks.GetValue(camera, _ => new DeviceClock());
        }

        public void Update(TimeSpan offset, DateTimeOffset measuredAt)
        {
            lock (_sync)
            {
                _offset = offset;
                _lastSynchronized = measuredAt;
            }
        }

        #endregion
    }
}
