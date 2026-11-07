// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;

namespace Onvif.Open.Core.Imaging
{
    public enum IrCutFilterMode
    {
        /// <summary>Filter in: colour (day) image.</summary>
        On = 1,

        /// <summary>Filter out: black and white (night) image with IR sensitivity.</summary>
        Off = 2,

        /// <summary>The camera switches by itself depending on the light.</summary>
        Auto = 3
    }

    /// <summary>Automatic or manual control of a feature (exposure, focus, white balance).</summary>
    public enum ImagingControlMode
    {
        Auto = 1,
        Manual = 2
    }

    /// <summary>
    /// Image settings of a video source. Values are in the device's own ranges (see <see cref="ImagingOptions"/>).
    /// When updating, only the properties that are set are sent; null properties are left unchanged on the device.
    /// </summary>
    public sealed class ImagingSettings
    {
        #region Public Variables

        public double? Brightness { get; set; }
        public double? ColorSaturation { get; set; }
        public double? Contrast { get; set; }
        public double? Sharpness { get; set; }

        /// <summary>Day/night: IR cut filter on (colour), off (night) or automatic.</summary>
        public IrCutFilterMode? IrCutFilter { get; set; }

        public bool? BacklightCompensation { get; set; }
        public bool? WideDynamicRange { get; set; }
        public double? WideDynamicRangeLevel { get; set; }
        public ImagingControlMode? Exposure { get; set; }
        public ImagingControlMode? Focus { get; set; }
        public ImagingControlMode? WhiteBalance { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsEmpty =>
            !Brightness.HasValue && !ColorSaturation.HasValue && !Contrast.HasValue && !Sharpness.HasValue &&
            !IrCutFilter.HasValue && !BacklightCompensation.HasValue && !WideDynamicRange.HasValue &&
            !WideDynamicRangeLevel.HasValue && !Exposure.HasValue && !Focus.HasValue && !WhiteBalance.HasValue;

        #endregion
    }

    /// <summary>A supported value range.</summary>
    public sealed class ImagingRange
    {
        #region Constructor

        public ImagingRange(double min, double max)
        {
            Min = min;
            Max = max;
        }

        #endregion

        #region Public Variables

        public double Min { get; }
        public double Max { get; }

        #endregion

        #region Public Methods

        public bool Contains(double value)
        {
            return value >= Min && value <= Max;
        }

        /// <summary>Maps 0..1 onto the range, e.g. to express "50 %" independently of the device's scale.</summary>
        public double FromFraction(double fraction)
        {
            return Min + (Math.Max(0, Math.Min(1, fraction)) * (Max - Min));
        }

        public override string ToString()
        {
            return $"{Min}..{Max}";
        }

        #endregion
    }

    /// <summary>What the video source supports; null ranges are not adjustable.</summary>
    public sealed class ImagingOptions
    {
        #region Public Variables

        public ImagingRange Brightness { get; internal set; }
        public ImagingRange ColorSaturation { get; internal set; }
        public ImagingRange Contrast { get; internal set; }
        public ImagingRange Sharpness { get; internal set; }
        public ImagingRange WideDynamicRangeLevel { get; internal set; }
        public IReadOnlyList<IrCutFilterMode> IrCutFilterModes { get; internal set; } = Array.Empty<IrCutFilterMode>();
        public bool SupportsBacklightCompensation { get; internal set; }
        public bool SupportsWideDynamicRange { get; internal set; }
        public IReadOnlyList<ImagingControlMode> ExposureModes { get; internal set; } = Array.Empty<ImagingControlMode>();
        public IReadOnlyList<ImagingControlMode> FocusModes { get; internal set; } = Array.Empty<ImagingControlMode>();
        public IReadOnlyList<ImagingControlMode> WhiteBalanceModes { get; internal set; } = Array.Empty<ImagingControlMode>();

        #endregion
    }

    public enum FocusMoveState
    {
        Unknown = 0,
        Idle = 1,
        Moving = 2
    }

    public sealed class FocusStatus
    {
        #region Constructor

        public FocusStatus(double? position, FocusMoveState moveState, string error)
        {
            Position = position;
            MoveState = moveState;
            Error = error;
        }

        #endregion

        #region Public Variables

        public double? Position { get; }
        public FocusMoveState MoveState { get; }
        public string Error { get; }

        #endregion
    }
}
