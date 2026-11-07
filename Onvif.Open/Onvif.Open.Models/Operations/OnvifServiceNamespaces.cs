// Sarthak Killedar
// 2026-11-07

namespace Onvif.Open.Core.Operations
{
    /// <summary>
    /// Namespaces identifying ONVIF services in the device's GetServices response.
    /// </summary>
    public static class OnvifServiceNamespaces
    {
        public const string Device = "http://www.onvif.org/ver10/device/wsdl";
        public const string Media = "http://www.onvif.org/ver10/media/wsdl";
        public const string Media2 = "http://www.onvif.org/ver20/media/wsdl";
        public const string Events = "http://www.onvif.org/ver10/events/wsdl";
        public const string Ptz = "http://www.onvif.org/ver20/ptz/wsdl";
        public const string Imaging = "http://www.onvif.org/ver20/imaging/wsdl";
        public const string DeviceIo = "http://www.onvif.org/ver10/deviceIO/wsdl";
        public const string Recording = "http://www.onvif.org/ver10/recording/wsdl";
        public const string Search = "http://www.onvif.org/ver10/search/wsdl";
        public const string Replay = "http://www.onvif.org/ver10/replay/wsdl";
    }

    /// <summary>
    /// ONVIF media service generation.
    /// </summary>
    public enum MediaServiceVersion
    {
        /// <summary>Media2 when the device offers it, otherwise Media1.</summary>
        Auto = 0,

        /// <summary>ver10 media (Profile S).</summary>
        Media1 = 1,

        /// <summary>ver20 media (Profile T): H.265 aware.</summary>
        Media2 = 2
    }
}
