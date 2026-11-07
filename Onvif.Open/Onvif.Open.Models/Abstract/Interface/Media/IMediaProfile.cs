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
    }
}
