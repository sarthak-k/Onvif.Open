// Sarthak Killedar
// Subset of the ONVIF schema (http://www.onvif.org/ver10/schema) needed for live streaming.
// Unknown elements returned by devices are ignored by the XmlSerializer, so the types stay tolerant
// to vendor extensions and newer schema versions. Enumerations are kept as strings for the same reason.

using System.Xml.Serialization;

namespace Onvif.Open.Media.Ver10
{
    internal static class OnvifNamespaces
    {
        public const string Schema = "http://www.onvif.org/ver10/schema";
        public const string Media = "http://www.onvif.org/ver10/media/wsdl";
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class Profile
    {
        [XmlAttribute("token")]
        public string Token { get; set; }

        [XmlAttribute("fixed")]
        public bool Fixed { get; set; }

        [XmlIgnore]
        public bool FixedSpecified { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }

        [XmlElement("VideoSourceConfiguration")]
        public VideoSourceConfiguration VideoSourceConfiguration { get; set; }

        [XmlElement("VideoEncoderConfiguration")]
        public VideoEncoderConfiguration VideoEncoderConfiguration { get; set; }

        [XmlElement("PTZConfiguration")]
        public PtzConfiguration PTZConfiguration { get; set; }

        /// <summary>Present when the profile streams analytics metadata (ONVIF Profile M / T).</summary>
        [XmlElement("MetadataConfiguration")]
        public MetadataConfiguration MetadataConfiguration { get; set; }
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class MetadataConfiguration
    {
        [XmlAttribute("token")]
        public string Token { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class PtzConfiguration
    {
        [XmlAttribute("token")]
        public string Token { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }

        [XmlElement("NodeToken")]
        public string NodeToken { get; set; }
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class VideoSourceConfiguration
    {
        [XmlAttribute("token")]
        public string Token { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }

        [XmlElement("SourceToken")]
        public string SourceToken { get; set; }
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class VideoEncoderConfiguration
    {
        [XmlAttribute("token")]
        public string Token { get; set; }

        [XmlElement("Name")]
        public string Name { get; set; }

        [XmlElement("Encoding")]
        public string Encoding { get; set; }

        [XmlElement("Resolution")]
        public VideoResolution Resolution { get; set; }

        [XmlElement("Quality")]
        public float Quality { get; set; }

        [XmlElement("RateControl")]
        public VideoRateControl RateControl { get; set; }
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class VideoResolution
    {
        [XmlElement("Width")]
        public int Width { get; set; }

        [XmlElement("Height")]
        public int Height { get; set; }
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class VideoRateControl
    {
        [XmlElement("FrameRateLimit")]
        public int FrameRateLimit { get; set; }

        [XmlElement("EncodingInterval")]
        public int EncodingInterval { get; set; }

        [XmlElement("BitrateLimit")]
        public int BitrateLimit { get; set; }
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class StreamSetup
    {
        /// <summary>"RTP-Unicast" or "RTP-Multicast".</summary>
        [XmlElement("Stream", Order = 0)]
        public string Stream { get; set; }

        [XmlElement("Transport", Order = 1)]
        public Transport Transport { get; set; }
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class Transport
    {
        /// <summary>"UDP", "TCP", "RTSP" or "HTTP".</summary>
        [XmlElement("Protocol")]
        public string Protocol { get; set; }
    }

    [XmlType(Namespace = OnvifNamespaces.Schema)]
    public class MediaUri
    {
        [XmlElement("Uri", DataType = "anyURI")]
        public string Uri { get; set; }

        [XmlElement("InvalidAfterConnect")]
        public bool InvalidAfterConnect { get; set; }

        [XmlElement("InvalidAfterReboot")]
        public bool InvalidAfterReboot { get; set; }

        [XmlElement("Timeout", DataType = "duration")]
        public string Timeout { get; set; }
    }
}
