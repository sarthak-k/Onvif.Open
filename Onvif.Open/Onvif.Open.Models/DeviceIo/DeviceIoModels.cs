// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.DeviceIo
{
    /// <summary>Monostable relays return to idle after their delay; bistable relays keep their state.</summary>
    public enum RelayMode
    {
        Monostable = 1,
        Bistable = 2
    }

    /// <summary>Electrical state of a relay (or input) when it is inactive.</summary>
    public enum IdleState
    {
        Closed = 1,
        Open = 2
    }

    public sealed class RelayOutput
    {
        #region Constructor

        public RelayOutput(string token, RelayMode mode, TimeSpan? delayTime, IdleState idleState)
        {
            Token = token;
            Mode = mode;
            DelayTime = delayTime;
            IdleState = idleState;
        }

        #endregion

        #region Public Variables

        public string Token { get; }
        public RelayMode Mode { get; }

        /// <summary>Monostable relays: how long they stay active.</summary>
        public TimeSpan? DelayTime { get; }

        public IdleState IdleState { get; }

        #endregion
    }

    public sealed class DigitalInput
    {
        #region Constructor

        public DigitalInput(string token, IdleState? idleState)
        {
            Token = token;
            IdleState = idleState;
        }

        #endregion

        #region Public Variables

        public string Token { get; }
        public IdleState? IdleState { get; }

        #endregion
    }

    /// <summary>A digital input (or relay) changed state, from the device's events.</summary>
    public sealed class IoStateChange
    {
        #region Constructor

        public IoStateChange(string token, bool isActive, bool isInitialState, DateTimeOffset utcTime)
        {
            Token = token;
            IsActive = isActive;
            IsInitialState = isInitialState;
            UtcTime = utcTime;
        }

        #endregion

        #region Public Variables

        /// <summary>Input (or relay) token.</summary>
        public string Token { get; }

        public bool IsActive { get; }

        /// <summary>True for the current state reported on subscribing, rather than a change.</summary>
        public bool IsInitialState { get; }

        public DateTimeOffset UtcTime { get; }

        #endregion
    }
}
