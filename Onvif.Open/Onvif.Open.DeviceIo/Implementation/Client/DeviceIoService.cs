// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.DeviceIo;
using Onvif.Open.Core.DeviceIo;
using Onvif.Open.Core.Implementation;
using Onvif.Open.DeviceIo.Ver10;

namespace Onvif.Open.DeviceIo.Implementation.Client
{
    /// <summary>
    /// Client for the ONVIF DeviceIO (ver10) service.
    /// </summary>
    public sealed class DeviceIoService : IDeviceIoService
    {
        #region Constructor

        private DeviceIoService(Uri serviceUri, ICameraInformation camera)
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

        public async Task<IReadOnlyList<DigitalInput>> GetDigitalInputsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = new DeviceIoClient(OnvifBindingFactory.CreateBinding(ServiceUri), new EndpointAddress(ServiceUri));
            var authenticator = PasswordDigestBuilder.GetBuilder().SetCamera(_camera).Build();
            client.Endpoint.EndpointBehaviors.Add(authenticator);
            authenticator.ConfigureCredentials(client.ClientCredentials);

            using (cancellationToken.Register(client.Abort))
            {
                try
                {
                    var response = await client.Port.GetDigitalInputsAsync(new GetDigitalInputsRequest()).ConfigureAwait(false);

                    return (response?.DigitalInputs ?? Array.Empty<DigitalInputType>())
                        .Where(i => !string.IsNullOrEmpty(i.Token))
                        .Select(i => new DigitalInput(i.Token, ToIdleState(i.IdleState)))
                        .ToList();
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

        internal static IDeviceIoService CreateInstance(Uri serviceUri, ICameraInformation camera)
        {
            return new DeviceIoService(serviceUri, camera);
        }

        #endregion

        #region Private Methods

        private static IdleState? ToIdleState(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "closed":
                    return IdleState.Closed;
                case "open":
                    return IdleState.Open;
                default:
                    return null;
            }
        }

        #endregion
    }
}
