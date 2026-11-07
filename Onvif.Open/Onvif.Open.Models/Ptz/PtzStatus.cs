// Sarthak Killedar
// 2026-11-07

using System;

namespace Onvif.Open.Core.Ptz
{
    public enum PtzMoveState
    {
        Unknown = 0,
        Idle = 1,
        Moving = 2
    }

    public sealed class PtzStatus
    {
        #region Constructor

        public PtzStatus(PtzVector position, PtzMoveState panTilt, PtzMoveState zoom, string error, DateTimeOffset? utcTime)
        {
            Position = position;
            PanTilt = panTilt;
            Zoom = zoom;
            Error = error;
            UtcTime = utcTime;
        }

        #endregion

        #region Public Variables

        /// <summary>Current position; null when the device does not report it.</summary>
        public PtzVector Position { get; }

        public PtzMoveState PanTilt { get; }
        public PtzMoveState Zoom { get; }
        public bool IsMoving => PanTilt == PtzMoveState.Moving || Zoom == PtzMoveState.Moving;
        public string Error { get; }
        public DateTimeOffset? UtcTime { get; }

        #endregion
    }

    public sealed class PtzPreset
    {
        #region Constructor

        public PtzPreset(string token, string name, PtzVector position)
        {
            Token = token;
            Name = name;
            Position = position;
        }

        #endregion

        #region Public Variables

        public string Token { get; }
        public string Name { get; }
        public PtzVector Position { get; }

        #endregion
    }
}
