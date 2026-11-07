// Sarthak Killedar
// 2026-11-07

using Onvif.Open.Core.Abstract.Interface.Media;
using Onvif.Open.Media.Ver10;

namespace Onvif.Open.Media.Implementation.Client
{
    public sealed class MediaProfile : IMediaProfile
    {
        #region Constructor

        private MediaProfile()
        {

        }

        #endregion

        #region Public Variables

        public string Token { get; private set; }
        public string Name { get; private set; }
        public string VideoEncoding { get; private set; }
        public int? Width { get; private set; }
        public int? Height { get; private set; }
        public int? FrameRateLimit { get; private set; }
        public int? BitrateLimit { get; private set; }
        public string PtzConfigurationToken { get; private set; }
        public string VideoSourceToken { get; private set; }

        #endregion

        #region Public Methods

        public override string ToString()
        {
            var resolution = Width.HasValue && Height.HasValue ? $" {Width}x{Height}" : string.Empty;
            var fps = FrameRateLimit.HasValue ? $" @{FrameRateLimit}fps" : string.Empty;
            return $"{Name} [{Token}] {VideoEncoding}{resolution}{fps}".Trim();
        }

        internal static IMediaProfile CreateInstance(Profile profile)
        {
            var encoder = profile.VideoEncoderConfiguration;

            return new MediaProfile
            {
                Token = profile.Token,
                Name = profile.Name,
                VideoEncoding = encoder?.Encoding,
                Width = encoder?.Resolution?.Width,
                Height = encoder?.Resolution?.Height,
                FrameRateLimit = encoder?.RateControl?.FrameRateLimit,
                BitrateLimit = encoder?.RateControl?.BitrateLimit,
                PtzConfigurationToken = profile.PTZConfiguration?.Token,
                VideoSourceToken = profile.VideoSourceConfiguration?.SourceToken
            };
        }

        internal static IMediaProfile CreateInstance(Ver20.MediaProfile2 profile)
        {
            var encoder = profile.Configurations?.VideoEncoder;

            return new MediaProfile
            {
                Token = profile.Token,
                Name = profile.Name,
                VideoEncoding = encoder?.Encoding,
                Width = encoder?.Resolution?.Width,
                Height = encoder?.Resolution?.Height,
                FrameRateLimit = encoder?.RateControl == null ? (int?)null : (int)System.Math.Round(encoder.RateControl.FrameRateLimit),
                BitrateLimit = encoder?.RateControl?.BitrateLimit,
                PtzConfigurationToken = profile.Configurations?.Ptz?.Token,
                VideoSourceToken = profile.Configurations?.VideoSource?.SourceToken
            };
        }

        #endregion
    }
}
