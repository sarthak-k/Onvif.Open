// Sarthak Killedar
// 2026-11-07

using System;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Imaging;
using Onvif.Open.Core.Imaging;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Imaging.Ver20;

namespace Onvif.Open.Imaging.Implementation.Client
{
    /// <summary>
    /// Client for the ONVIF Imaging (ver20) service.
    /// </summary>
    public sealed class ImagingService : IImagingService
    {
        #region Constructor

        private ImagingService(Uri serviceUri, ICameraInformation camera)
        {
            ServiceUri = serviceUri;
            _camera = camera;
        }

        #endregion

        #region Private Variables

        private readonly ICameraInformation _camera;

        #endregion

        #region Public Variables

        public Uri ServiceUri { get; }

        #endregion

        #region Public Methods

        public async Task<ImagingSettings> GetImagingSettingsAsync(string videoSourceToken, CancellationToken cancellationToken = default)
        {
            var request = new GetImagingSettingsRequest { VideoSourceToken = Require(videoSourceToken) };
            var response = await InvokeAsync(port => port.GetImagingSettingsAsync(request), cancellationToken).ConfigureAwait(false);
            return ImagingMapper.FromWire(response?.ImagingSettings);
        }

        public Task SetImagingSettingsAsync(string videoSourceToken, ImagingSettings settings, bool forcePersistence = true, CancellationToken cancellationToken = default)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (settings.IsEmpty)
            {
                return Task.CompletedTask;
            }

            var request = new SetImagingSettingsRequest
            {
                VideoSourceToken = Require(videoSourceToken),
                ImagingSettings = ImagingMapper.ToWire(settings),
                ForcePersistence = forcePersistence
            };

            return InvokeAsync(port => port.SetImagingSettingsAsync(request), cancellationToken);
        }

        public async Task<ImagingOptions> GetOptionsAsync(string videoSourceToken, CancellationToken cancellationToken = default)
        {
            var request = new GetOptionsRequest { VideoSourceToken = Require(videoSourceToken) };
            var response = await InvokeAsync(port => port.GetOptionsAsync(request), cancellationToken).ConfigureAwait(false);
            return ImagingMapper.FromWire(response?.ImagingOptions);
        }

        public Task MoveFocusContinuousAsync(string videoSourceToken, double speed, CancellationToken cancellationToken = default)
        {
            return MoveAsync(videoSourceToken, new FocusMove { Continuous = new ContinuousFocus { Speed = (float)speed } }, cancellationToken);
        }

        public Task MoveFocusAbsoluteAsync(string videoSourceToken, double position, double? speed = null, CancellationToken cancellationToken = default)
        {
            return MoveAsync(videoSourceToken, new FocusMove
            {
                Absolute = new AbsoluteFocus { Position = (float)position, Speed = (float)(speed ?? 0), SpeedSpecified = speed.HasValue }
            }, cancellationToken);
        }

        public Task MoveFocusRelativeAsync(string videoSourceToken, double distance, double? speed = null, CancellationToken cancellationToken = default)
        {
            return MoveAsync(videoSourceToken, new FocusMove
            {
                Relative = new RelativeFocus { Distance = (float)distance, Speed = (float)(speed ?? 0), SpeedSpecified = speed.HasValue }
            }, cancellationToken);
        }

        public Task StopFocusAsync(string videoSourceToken, CancellationToken cancellationToken = default)
        {
            var request = new StopRequest { VideoSourceToken = Require(videoSourceToken) };
            return InvokeAsync(port => port.StopAsync(request), cancellationToken);
        }

        public async Task<FocusStatus> GetFocusStatusAsync(string videoSourceToken, CancellationToken cancellationToken = default)
        {
            var request = new GetStatusRequest { VideoSourceToken = Require(videoSourceToken) };
            var status = (await InvokeAsync(port => port.GetStatusAsync(request), cancellationToken).ConfigureAwait(false))?.Status?.FocusStatus20;

            FocusMoveState state;
            switch ((status?.MoveStatus ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "IDLE":
                    state = FocusMoveState.Idle;
                    break;
                case "MOVING":
                    state = FocusMoveState.Moving;
                    break;
                default:
                    state = FocusMoveState.Unknown;
                    break;
            }

            return new FocusStatus(status?.PositionSpecified == true ? status.Position : (double?)null, state, string.IsNullOrWhiteSpace(status?.Error) ? null : status.Error);
        }

        internal static IImagingService CreateInstance(Uri serviceUri, ICameraInformation camera)
        {
            return new ImagingService(serviceUri, camera);
        }

        #endregion

        #region Private Methods

        private Task MoveAsync(string videoSourceToken, FocusMove focus, CancellationToken cancellationToken)
        {
            var request = new MoveRequest { VideoSourceToken = Require(videoSourceToken), Focus = focus };
            return InvokeAsync(port => port.MoveAsync(request), cancellationToken);
        }

        private static string Require(string videoSourceToken)
        {
            if (string.IsNullOrWhiteSpace(videoSourceToken))
            {
                throw new ArgumentNullException(nameof(videoSourceToken));
            }

            return videoSourceToken;
        }

        private async Task<T> InvokeAsync<T>(Func<IImagingPort, Task<T>> call, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = new ImagingClient(OnvifBindingFactory.CreateBinding(ServiceUri), new EndpointAddress(ServiceUri));
            var authenticator = PasswordDigestBuilder.GetBuilder().SetCamera(_camera).Build();
            client.Endpoint.EndpointBehaviors.Add(authenticator);
            authenticator.ConfigureCredentials(client.ClientCredentials);

            using (cancellationToken.Register(client.Abort))
            {
                try
                {
                    return await call(client.Port).ConfigureAwait(false);
                }
                catch (CommunicationException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                finally
                {
                    try
                    {
                        if (client.State == CommunicationState.Opened)
                        {
                            client.Close();
                        }
                        else
                        {
                            client.Abort();
                        }
                    }
                    catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException)
                    {
                        client.Abort();
                    }
                }
            }
        }

        #endregion
    }
}
