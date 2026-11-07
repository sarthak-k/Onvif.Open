// Sarthak Killedar
// 2026-11-07
// Subset of the ONVIF schema used by the PTZ service. Vectors are attributes (x, y, space) as in tt:Vector2D / tt:Vector1D.

using System.Xml.Serialization;

namespace Onvif.Open.Ptz.Ver20
{
    internal static class PtzNamespaces
    {
        public const string Schema = "http://www.onvif.org/ver10/schema";
        public const string Ptz = "http://www.onvif.org/ver20/ptz/wsdl";
    }

    [XmlType(Namespace = PtzNamespaces.Schema)]
    public class PtzVectorType
    {
        [XmlElement("PanTilt")]
        public Vector2D PanTilt { get; set; }

        [XmlElement("Zoom")]
        public Vector1D Zoom { get; set; }
    }

    [XmlType(Namespace = PtzNamespaces.Schema)]
    public class Vector2D
    {
        [XmlAttribute("x")]
        public float X { get; set; }

        [XmlAttribute("y")]
        public float Y { get; set; }

        [XmlAttribute("space", DataType = "anyURI")]
        public string Space { get; set; }
    }

    [XmlType(Namespace = PtzNamespaces.Schema)]
    public class Vector1D
    {
        [XmlAttribute("x")]
        public float X { get; set; }

        [XmlAttribute("space", DataType = "anyURI")]
        public string Space { get; set; }
    }

    [XmlType(Namespace = PtzNamespaces.Schema)]
    public class PtzStatusType
    {
        [XmlElement("Position")]
        public PtzVectorType Position { get; set; }

        [XmlElement("MoveStatus")]
        public PtzMoveStatus MoveStatus { get; set; }

        [XmlElement("Error")]
        public string Error { get; set; }

        [XmlElement("UtcTime")]
        public string UtcTime { get; set; }
    }

    [XmlType(Namespace = PtzNamespaces.Schema)]
    public class PtzMoveStatus
    {
        /// <summary>"IDLE", "MOVING" or "UNKNOWN".</summary>
        [XmlElement("PanTilt")]
        public string PanTilt { get; set; }

        [XmlElement("Zoom")]
        public string Zoom { get; set; }
    }

    [XmlType(Namespace = PtzNamespaces.Schema)]
    public class PtzPresetType
    {
        [XmlAttribute("token")]
        public string Token { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }

        [XmlElement("PTZPosition")]
        public PtzVectorType PTZPosition { get; set; }
    }
}
