// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Ptz;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Core.Ptz;
using Onvif.Open.Ptz.Ver20;

namespace Onvif.Open.Ptz.Implementation.Client
{
    /// <summary>
    /// Client for the ONVIF PTZ (ver20) service.
    /// </summary>
    public sealed class PtzService : IPtzService
    {
        #region Constructor

        private PtzService(Uri serviceUri, ICameraInformation camera)
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

        public Task ContinuousMoveAsync(string profileToken, PtzVector velocity, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var request = new ContinuousMoveRequest
            {
                ProfileToken = Require(profileToken),
                Velocity = ToVector(velocity ?? throw new ArgumentNullException(nameof(velocity))),
                Timeout = timeout.HasValue ? XmlConvert.ToString(timeout.Value) : null
            };

            return InvokeAsync(port => port.ContinuousMoveAsync(request), cancellationToken);
        }

        public Task RelativeMoveAsync(string profileToken, PtzVector translation, PtzVector speed = null, CancellationToken cancellationToken = default)
        {
            var request = new RelativeMoveRequest
            {
                ProfileToken = Require(profileToken),
                Translation = ToVector(translation ?? throw new ArgumentNullException(nameof(translation))),
                Speed = ToVector(speed)
            };

            return InvokeAsync(port => port.RelativeMoveAsync(request), cancellationToken);
        }

        public Task AbsoluteMoveAsync(string profileToken, PtzVector position, PtzVector speed = null, CancellationToken cancellationToken = default)
        {
            var request = new AbsoluteMoveRequest
            {
                ProfileToken = Require(profileToken),
                Position = ToVector(position ?? throw new ArgumentNullException(nameof(position))),
                Speed = ToVector(speed)
            };

            return InvokeAsync(port => port.AbsoluteMoveAsync(request), cancellationToken);
        }

        public Task StopAsync(string profileToken, bool panTilt = true, bool zoom = true, CancellationToken cancellationToken = default)
        {
            var request = new StopRequest { ProfileToken = Require(profileToken), PanTilt = panTilt, Zoom = zoom };
            return InvokeAsync(port => port.StopAsync(request), cancellationToken);
        }

        public async Task<PtzStatus> GetStatusAsync(string profileToken, CancellationToken cancellationToken = default)
        {
            var request = new GetStatusRequest { ProfileToken = Require(profileToken) };
            var status = (await InvokeAsync(port => port.GetStatusAsync(request), cancellationToken).ConfigureAwait(false))?.PTZStatus;

            return new PtzStatus(
                FromVector(status?.Position),
                ToMoveState(status?.MoveStatus?.PanTilt),
                ToMoveState(status?.MoveStatus?.Zoom),
                string.IsNullOrWhiteSpace(status?.Error) ? null : status.Error,
                DateTimeOffset.TryParse(status?.UtcTime, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time) ? time : (DateTimeOffset?)null);
        }

        public async Task<IReadOnlyList<PtzPreset>> GetPresetsAsync(string profileToken, CancellationToken cancellationToken = default)
        {
            var request = new GetPresetsRequest { ProfileToken = Require(profileToken) };
            var response = await InvokeAsync(port => port.GetPresetsAsync(request), cancellationToken).ConfigureAwait(false);

            return (response?.Preset ?? Array.Empty<PtzPresetType>())
                .Where(p => !string.IsNullOrEmpty(p.Token))
                .Select(p => new PtzPreset(p.Token, p.Name, FromVector(p.PTZPosition)))
                .ToList();
        }

        public Task GotoPresetAsync(string profileToken, string presetToken, PtzVector speed = null, CancellationToken cancellationToken = default)
        {
            var request = new GotoPresetRequest { ProfileToken = Require(profileToken), PresetToken = Require(presetToken, nameof(presetToken)), Speed = ToVector(speed) };
            return InvokeAsync(port => port.GotoPresetAsync(request), cancellationToken);
        }

        public async Task<string> SetPresetAsync(string profileToken, string presetName, string presetToken = null, CancellationToken cancellationToken = default)
        {
            var request = new SetPresetRequest { ProfileToken = Require(profileToken), PresetName = presetName, PresetToken = presetToken };
            var response = await InvokeAsync(port => port.SetPresetAsync(request), cancellationToken).ConfigureAwait(false);
            return response?.PresetToken ?? presetToken;
        }

        public Task RemovePresetAsync(string profileToken, string presetToken, CancellationToken cancellationToken = default)
        {
            var request = new RemovePresetRequest { ProfileToken = Require(profileToken), PresetToken = Require(presetToken, nameof(presetToken)) };
            return InvokeAsync(port => port.RemovePresetAsync(request), cancellationToken);
        }

        public Task GotoHomePositionAsync(string profileToken, PtzVector speed = null, CancellationToken cancellationToken = default)
        {
            var request = new GotoHomePositionRequest { ProfileToken = Require(profileToken), Speed = ToVector(speed) };
            return InvokeAsync(port => port.GotoHomePositionAsync(request), cancellationToken);
        }

        public Task SetHomePositionAsync(string profileToken, CancellationToken cancellationToken = default)
        {
            var request = new SetHomePositionRequest { ProfileToken = Require(profileToken) };
            return InvokeAsync(port => port.SetHomePositionAsync(request), cancellationToken);
        }

        internal static IPtzService CreateInstance(Uri serviceUri, ICameraInformation camera)
        {
            return new PtzService(serviceUri, camera);
        }

        internal static PtzVectorType ToVector(PtzVector vector)
        {
            if (vector == null || (!vector.HasPanTilt && !vector.Zoom.HasValue))
            {
                return null;
            }

            return new PtzVectorType
            {
                PanTilt = vector.HasPanTilt ? new Vector2D { X = (float)(vector.Pan ?? 0), Y = (float)(vector.Tilt ?? 0) } : null,
                Zoom = vector.Zoom.HasValue ? new Vector1D { X = (float)vector.Zoom.Value } : null
            };
        }

        internal static PtzVector FromVector(PtzVectorType vector)
        {
            if (vector == null || (vector.PanTilt == null && vector.Zoom == null))
            {
                return null;
            }

            return new PtzVector(
                vector.PanTilt == null ? (double?)null : Math.Round(vector.PanTilt.X, 6),
                vector.PanTilt == null ? (double?)null : Math.Round(vector.PanTilt.Y, 6),
                vector.Zoom == null ? (double?)null : Math.Round(vector.Zoom.X, 6));
        }

        #endregion

        #region Private Methods

        private static PtzMoveState ToMoveState(string state)
        {
            switch ((state ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "IDLE":
                    return PtzMoveState.Idle;
                case "MOVING":
                    return PtzMoveState.Moving;
                default:
                    return PtzMoveState.Unknown;
            }
        }

        private static string Require(string value, string name = "profileToken")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentNullException(name);
            }

            return value;
        }

        private async Task<T> InvokeAsync<T>(Func<IPtzPort, Task<T>> call, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = new PtzClient(OnvifBindingFactory.CreateBinding(ServiceUri), new EndpointAddress(ServiceUri));
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
