// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using Onvif.Open.Core.Imaging;
using Onvif.Open.Imaging.Ver20;

namespace Onvif.Open.Imaging.Implementation.Client
{
    /// <summary>
    /// Converts between the public imaging model and the ONVIF wire types.
    /// </summary>
    internal static class ImagingMapper
    {
        #region Public Methods

        public static ImagingSettings20 ToWire(ImagingSettings settings)
        {
            var wire = new ImagingSettings20();

            if (settings.Brightness.HasValue)
            {
                wire.Brightness = (float)settings.Brightness.Value;
                wire.BrightnessSpecified = true;
            }

            if (settings.ColorSaturation.HasValue)
            {
                wire.ColorSaturation = (float)settings.ColorSaturation.Value;
                wire.ColorSaturationSpecified = true;
            }

            if (settings.Contrast.HasValue)
            {
                wire.Contrast = (float)settings.Contrast.Value;
                wire.ContrastSpecified = true;
            }

            if (settings.Sharpness.HasValue)
            {
                wire.Sharpness = (float)settings.Sharpness.Value;
                wire.SharpnessSpecified = true;
            }

            if (settings.IrCutFilter.HasValue)
            {
                wire.IrCutFilter = settings.IrCutFilter.Value.ToString().ToUpperInvariant();
            }

            if (settings.BacklightCompensation.HasValue)
            {
                wire.BacklightCompensation = new ModeAndLevel { Mode = OnOff(settings.BacklightCompensation.Value) };
            }

            if (settings.WideDynamicRange.HasValue || settings.WideDynamicRangeLevel.HasValue)
            {
                wire.WideDynamicRange = new ModeAndLevel
                {
                    // ONVIF requires the mode; a level change alone implies WDR on.
                    Mode = OnOff(settings.WideDynamicRange ?? true),
                    Level = (float)(settings.WideDynamicRangeLevel ?? 0),
                    LevelSpecified = settings.WideDynamicRangeLevel.HasValue
                };
            }

            if (settings.Exposure.HasValue)
            {
                wire.Exposure = new ModeOnly { Mode = AutoManual(settings.Exposure.Value) };
            }

            if (settings.Focus.HasValue)
            {
                wire.Focus = new FocusConfiguration { AutoFocusMode = AutoManual(settings.Focus.Value) };
            }

            if (settings.WhiteBalance.HasValue)
            {
                wire.WhiteBalance = new ModeOnly { Mode = AutoManual(settings.WhiteBalance.Value) };
            }

            return wire;
        }

        public static ImagingSettings FromWire(ImagingSettings20 wire)
        {
            if (wire == null)
            {
                return new ImagingSettings();
            }

            return new ImagingSettings
            {
                Brightness = wire.BrightnessSpecified ? wire.Brightness : (double?)null,
                ColorSaturation = wire.ColorSaturationSpecified ? wire.ColorSaturation : (double?)null,
                Contrast = wire.ContrastSpecified ? wire.Contrast : (double?)null,
                Sharpness = wire.SharpnessSpecified ? wire.Sharpness : (double?)null,
                IrCutFilter = ToIrCut(wire.IrCutFilter),
                BacklightCompensation = ToOnOff(wire.BacklightCompensation?.Mode),
                WideDynamicRange = ToOnOff(wire.WideDynamicRange?.Mode),
                WideDynamicRangeLevel = wire.WideDynamicRange?.LevelSpecified == true ? wire.WideDynamicRange.Level : (double?)null,
                Exposure = ToAutoManual(wire.Exposure?.Mode),
                Focus = ToAutoManual(wire.Focus?.AutoFocusMode),
                WhiteBalance = ToAutoManual(wire.WhiteBalance?.Mode)
            };
        }

        public static ImagingOptions FromWire(ImagingOptions20 wire)
        {
            if (wire == null)
            {
                return new ImagingOptions();
            }

            return new ImagingOptions
            {
                Brightness = ToRange(wire.Brightness),
                ColorSaturation = ToRange(wire.ColorSaturation),
                Contrast = ToRange(wire.Contrast),
                Sharpness = ToRange(wire.Sharpness),
                WideDynamicRangeLevel = ToRange(wire.WideDynamicRange?.Level),
                IrCutFilterModes = (wire.IrCutFilterModes ?? Array.Empty<string>()).Select(ToIrCut).Where(m => m.HasValue).Select(m => m.Value).Distinct().ToList(),
                SupportsBacklightCompensation = Supports(wire.BacklightCompensation?.Mode, "ON"),
                SupportsWideDynamicRange = Supports(wire.WideDynamicRange?.Mode, "ON"),
                ExposureModes = Modes(wire.Exposure?.Mode),
                FocusModes = Modes(wire.Focus?.AutoFocusModes),
                WhiteBalanceModes = Modes(wire.WhiteBalance?.Mode)
            };
        }

        #endregion

        #region Private Methods

        private static string OnOff(bool on) => on ? "ON" : "OFF";

        private static string AutoManual(ImagingControlMode mode) => mode == ImagingControlMode.Auto ? "AUTO" : "MANUAL";

        private static IrCutFilterMode? ToIrCut(string value)
        {
            switch (Normalize(value))
            {
                case "ON":
                    return IrCutFilterMode.On;
                case "OFF":
                    return IrCutFilterMode.Off;
                case "AUTO":
                    return IrCutFilterMode.Auto;
                default:
                    return null;
            }
        }

        private static bool? ToOnOff(string value)
        {
            switch (Normalize(value))
            {
                case "ON":
                    return true;
                case "OFF":
                    return false;
                default:
                    return null;
            }
        }

        private static ImagingControlMode? ToAutoManual(string value)
        {
            switch (Normalize(value))
            {
                case "AUTO":
                    return ImagingControlMode.Auto;
                case "MANUAL":
                    return ImagingControlMode.Manual;
                default:
                    return null;
            }
        }

        private static IReadOnlyList<ImagingControlMode> Modes(IEnumerable<string> values)
        {
            return (values ?? Array.Empty<string>()).Select(ToAutoManual).Where(m => m.HasValue).Select(m => m.Value).Distinct().ToList();
        }

        private static bool Supports(IEnumerable<string> modes, string mode)
        {
            return (modes ?? Array.Empty<string>()).Any(m => Normalize(m) == mode);
        }

        private static ImagingRange ToRange(FloatRange range)
        {
            return range == null ? null : new ImagingRange(range.Min, range.Max);
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }

        #endregion
    }
}
