// Sarthak Killedar
namespace Onvif.Open.Core.Abstract.Interface.Media
{
    /// <summary>
    /// A media profile exposed by the device (a combination of video source and encoder settings).
    /// </summary>
    public interface IMediaProfile
    {
        string Token { get; }
        string Name { get; }
        string VideoEncoding { get; }
        int? Width { get; }
        int? Height { get; }
        int? FrameRateLimit { get; }
        int? BitrateLimit { get; }

        /// <summary>Token of the profile's PTZ configuration; null when the profile has no PTZ.</summary>
        string PtzConfigurationToken { get; }

        /// <summary>Token of the physical video source (used by the Imaging service).</summary>
        string VideoSourceToken { get; }
    }
}
