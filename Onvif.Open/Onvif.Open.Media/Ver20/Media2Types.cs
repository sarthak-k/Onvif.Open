// Sarthak Killedar
// 2026-11-07
// Subset of the Media2 (ver20) schema used for live streaming. Profile and configuration-set elements are in the
// media2 namespace; the configurations themselves are ONVIF schema (tt) types. Unknown elements are ignored.

using System.Xml.Serialization;
using Onvif.Open.Media.Ver10;

namespace Onvif.Open.Media.Ver20
{
    internal static class Media2Namespaces
    {
        public const string Schema = "http://www.onvif.org/ver10/schema";
        public const string Media2 = "http://www.onvif.org/ver20/media/wsdl";
    }

    [XmlType(Namespace = Media2Namespaces.Media2)]
    public class MediaProfile2
    {
        [XmlAttribute("token")]
        public string Token { get; set; }

        [XmlAttribute("fixed")]
        public bool Fixed { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }

        [XmlElement("Configurations")]
        public ConfigurationSet Configurations { get; set; }
    }

    [XmlType(Namespace = Media2Namespaces.Media2)]
    public class ConfigurationSet
    {
        [XmlElement("VideoSource")]
        public VideoSourceConfiguration VideoSource { get; set; }

        [XmlElement("VideoEncoder")]
        public VideoEncoder2Configuration VideoEncoder { get; set; }

        [XmlElement("PTZ")]
        public PtzConfiguration Ptz { get; set; }
    }

    [XmlType(Namespace = Media2Namespaces.Schema)]
    public class VideoEncoder2Configuration
    {
        [XmlAttribute("token")]
        public string Token { get; set; }

        [XmlAttribute("GovLength")]
        public int GovLength { get; set; }

        [XmlIgnore]
        public bool GovLengthSpecified { get; set; }

        [XmlAttribute("Profile")]
        public string Profile { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }

        /// <summary>Media subtype, e.g. "H264", "H265", "JPEG".</summary>
        [XmlElement("Encoding")]
        public string Encoding { get; set; }

        [XmlElement("Resolution")]
        public VideoResolution Resolution { get; set; }

        [XmlElement("RateControl")]
        public VideoRateControl2 RateControl { get; set; }

        [XmlElement("Quality")]
        public float Quality { get; set; }
    }

    [XmlType(Namespace = Media2Namespaces.Schema)]
    public class VideoRateControl2
    {
        [XmlAttribute("ConstantBitRate")]
        public bool ConstantBitRate { get; set; }

        [XmlIgnore]
        public bool ConstantBitRateSpecified { get; set; }

        [XmlElement("FrameRateLimit")]
        public float FrameRateLimit { get; set; }

        [XmlElement("BitrateLimit")]
        public int BitrateLimit { get; set; }
    }
}
