// Sarthak Killedar
// 2026-11-07

using System;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Events;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Events.Ver10;

namespace Onvif.Open.Events.Implementation.Client
{
    /// <summary>
    /// Client for the ONVIF Events (ver10) service.
    /// </summary>
    public sealed class EventService : IEventService
    {
        #region Constructor

        private EventService(Uri serviceUri, ICameraInformation camera, TimeProvider timeProvider)
        {
            ServiceUri = serviceUri;
            _camera = camera;
            _timeProvider = timeProvider;
        }

        #endregion

        #region Private Variables

        private readonly ICameraInformation _camera;
        private readonly TimeProvider _timeProvider;

        #endregion

        #region Public Variables

        public Uri ServiceUri { get; }

        #endregion

        #region Public Methods

        public async Task<IPullPointSubscription> CreatePullPointSubscriptionAsync(TimeSpan terminationTime, CancellationToken cancellationToken = default)
        {
            var client = new EventClient(OnvifBindingFactory.CreateBinding(ServiceUri, useAddressing: true), new EndpointAddress(ServiceUri));
            ApplyAuthentication(client.Endpoint, client.ClientCredentials, _camera);

            try
            {
                using (cancellationToken.Register(client.Abort))
                {
                    var response = await client.Port.CreatePullPointSubscriptionAsync(new CreatePullPointSubscriptionRequest
                    {
                        InitialTerminationTime = XmlConvert.ToString(terminationTime)
                    }).ConfigureAwait(false);

                    if (response?.SubscriptionReference?.Elements == null)
                    {
                        throw new InvalidOperationException("The device did not return a subscription reference.");
                    }

                    var (address, parameters) = NotificationParser.ParseEndpointReference(response.SubscriptionReference.Elements);
                    var endpoint = new EndpointAddress(address, parameters.Select(p => (AddressHeader)new ReferenceParameterHeader(p)).ToArray());
                    return PullPointSubscription.CreateInstance(endpoint, _camera, _timeProvider);
                }
            }
            catch (CommunicationException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            finally
            {
                Close(client);
            }
        }

        internal static IEventService CreateInstance(Uri serviceUri, ICameraInformation camera, TimeProvider timeProvider)
        {
            return new EventService(serviceUri, camera, timeProvider);
        }

        internal static void ApplyAuthentication(System.ServiceModel.Description.ServiceEndpoint endpoint, System.ServiceModel.Description.ClientCredentials credentials, ICameraInformation camera)
        {
            var authenticator = PasswordDigestBuilder.GetBuilder().SetCamera(camera).Build();
            endpoint.EndpointBehaviors.Add(authenticator);
            authenticator.ConfigureCredentials(credentials);
        }

        internal static void Close(ICommunicationObject client)
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

        #endregion
    }
}
