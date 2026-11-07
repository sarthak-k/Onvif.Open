// Sarthak Killedar
// 2026-11-07
// Subset of tt:ImagingSettings20 / tt:ImagingOptions20. Settings are written in schema order and only the
// values that are specified (XxxSpecified) are sent, so unknown and unchanged settings are never touched.

using System.Xml.Serialization;

namespace Onvif.Open.Imaging.Ver20
{
    internal static class ImagingNamespaces
    {
        public const string Schema = "http://www.onvif.org/ver10/schema";
        public const string Imaging = "http://www.onvif.org/ver20/imaging/wsdl";
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class ImagingSettings20
    {
        [XmlElement("BacklightCompensation", Order = 0)]
        public ModeAndLevel BacklightCompensation { get; set; }

        [XmlElement("Brightness", Order = 1)]
        public float Brightness { get; set; }

        [XmlIgnore]
        public bool BrightnessSpecified { get; set; }

        [XmlElement("ColorSaturation", Order = 2)]
        public float ColorSaturation { get; set; }

        [XmlIgnore]
        public bool ColorSaturationSpecified { get; set; }

        [XmlElement("Contrast", Order = 3)]
        public float Contrast { get; set; }

        [XmlIgnore]
        public bool ContrastSpecified { get; set; }

        [XmlElement("Exposure", Order = 4)]
        public ModeOnly Exposure { get; set; }

        [XmlElement("Focus", Order = 5)]
        public FocusConfiguration Focus { get; set; }

        /// <summary>"ON", "OFF" or "AUTO".</summary>
        [XmlElement("IrCutFilter", Order = 6)]
        public string IrCutFilter { get; set; }

        [XmlElement("Sharpness", Order = 7)]
        public float Sharpness { get; set; }

        [XmlIgnore]
        public bool SharpnessSpecified { get; set; }

        [XmlElement("WideDynamicRange", Order = 8)]
        public ModeAndLevel WideDynamicRange { get; set; }

        [XmlElement("WhiteBalance", Order = 9)]
        public ModeOnly WhiteBalance { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class ModeAndLevel
    {
        [XmlElement("Mode", Order = 0)]
        public string Mode { get; set; }

        [XmlElement("Level", Order = 1)]
        public float Level { get; set; }

        [XmlIgnore]
        public bool LevelSpecified { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class ModeOnly
    {
        [XmlElement("Mode")]
        public string Mode { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class FocusConfiguration
    {
        [XmlElement("AutoFocusMode")]
        public string AutoFocusMode { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class ImagingOptions20
    {
        [XmlElement("BacklightCompensation")]
        public ModesAndRange BacklightCompensation { get; set; }

        [XmlElement("Brightness")]
        public FloatRange Brightness { get; set; }

        [XmlElement("ColorSaturation")]
        public FloatRange ColorSaturation { get; set; }

        [XmlElement("Contrast")]
        public FloatRange Contrast { get; set; }

        [XmlElement("Exposure")]
        public ModesAndRange Exposure { get; set; }

        [XmlElement("Focus")]
        public FocusOptions Focus { get; set; }

        [XmlElement("IrCutFilterModes")]
        public string[] IrCutFilterModes { get; set; }

        [XmlElement("Sharpness")]
        public FloatRange Sharpness { get; set; }

        [XmlElement("WideDynamicRange")]
        public ModesAndRange WideDynamicRange { get; set; }

        [XmlElement("WhiteBalance")]
        public ModesAndRange WhiteBalance { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class FloatRange
    {
        [XmlElement("Min")]
        public float Min { get; set; }

        [XmlElement("Max")]
        public float Max { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class ModesAndRange
    {
        [XmlElement("Mode")]
        public string[] Mode { get; set; }

        [XmlElement("Level")]
        public FloatRange Level { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class FocusOptions
    {
        [XmlElement("AutoFocusModes")]
        public string[] AutoFocusModes { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class FocusMove
    {
        [XmlElement("Absolute", Order = 0)]
        public AbsoluteFocus Absolute { get; set; }

        [XmlElement("Relative", Order = 1)]
        public RelativeFocus Relative { get; set; }

        [XmlElement("Continuous", Order = 2)]
        public ContinuousFocus Continuous { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class AbsoluteFocus
    {
        [XmlElement("Position", Order = 0)]
        public float Position { get; set; }

        [XmlElement("Speed", Order = 1)]
        public float Speed { get; set; }

        [XmlIgnore]
        public bool SpeedSpecified { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class RelativeFocus
    {
        [XmlElement("Distance", Order = 0)]
        public float Distance { get; set; }

        [XmlElement("Speed", Order = 1)]
        public float Speed { get; set; }

        [XmlIgnore]
        public bool SpeedSpecified { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class ContinuousFocus
    {
        [XmlElement("Speed")]
        public float Speed { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class ImagingStatus20
    {
        [XmlElement("FocusStatus20")]
        public FocusStatus20 FocusStatus20 { get; set; }
    }

    [XmlType(Namespace = ImagingNamespaces.Schema)]
    public class FocusStatus20
    {
        [XmlElement("Position")]
        public float Position { get; set; }

        [XmlIgnore]
        public bool PositionSpecified { get; set; }

        /// <summary>"IDLE", "MOVING" or "UNKNOWN".</summary>
        [XmlElement("MoveStatus")]
        public string MoveStatus { get; set; }

        [XmlElement("Error")]
        public string Error { get; set; }
    }
}
