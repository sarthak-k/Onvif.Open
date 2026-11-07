// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Events;
using Onvif.Open.Core.Events;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Events.Ver10;

namespace Onvif.Open.Events.Implementation.Client
{
    internal sealed class PullPointSubscription : IPullPointSubscription
    {
        #region Constructor

        private PullPointSubscription(EndpointAddress endpoint, ICameraInformation camera, TimeProvider timeProvider)
        {
            _endpoint = endpoint;
            _camera = camera;
            _timeProvider = timeProvider;
        }

        #endregion

        #region Private Variables

        /// <summary>The device holds PullMessages up to its timeout: allow for that plus the network.</summary>
        private static readonly TimeSpan NetworkAllowance = TimeSpan.FromSeconds(15);

        private readonly EndpointAddress _endpoint;
        private readonly ICameraInformation _camera;
        private readonly TimeProvider _timeProvider;
        private int _unsubscribed;

        #endregion

        #region Public Variables

        public Uri Address => _endpoint.Uri;

        #endregion

        #region Public Methods

        public async Task<IReadOnlyList<CameraEvent>> PullMessagesAsync(TimeSpan timeout, int messageLimit, string cameraId = null, CancellationToken cancellationToken = default)
        {
            var response = await InvokeAsync(port => port.PullMessagesAsync(new PullMessagesRequest
            {
                Timeout = XmlConvert.ToString(timeout),
                MessageLimit = messageLimit
            }), timeout + NetworkAllowance, cancellationToken).ConfigureAwait(false);

            return NotificationParser.Parse(response?.Elements, cameraId, _timeProvider.GetUtcNow());
        }

        public Task RenewAsync(TimeSpan terminationTime, CancellationToken cancellationToken = default)
        {
            return InvokeAsync(port => port.RenewAsync(new RenewRequest { TerminationTime = XmlConvert.ToString(terminationTime) }), NetworkAllowance, cancellationToken);
        }

        public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _unsubscribed, 1) == 1)
            {
                return;
            }

            await InvokeAsync(port => port.UnsubscribeAsync(new UnsubscribeRequest()), NetworkAllowance, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await UnsubscribeAsync(timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException || ex is OperationCanceledException)
            {
                // Best effort: the subscription expires on its own at its termination time.
            }
        }

        internal static IPullPointSubscription CreateInstance(EndpointAddress endpoint, ICameraInformation camera, TimeProvider timeProvider)
        {
            return new PullPointSubscription(endpoint, camera, timeProvider);
        }

        #endregion

        #region Private Methods

        private async Task<T> InvokeAsync<T>(Func<IPullPointSubscriptionPort, Task<T>> call, TimeSpan sendTimeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var binding = OnvifBindingFactory.CreateBinding(_endpoint.Uri, useAddressing: true);
            binding.SendTimeout = sendTimeout;
            binding.ReceiveTimeout = sendTimeout;

            var client = new PullPointSubscriptionClient(binding, _endpoint);
            EventService.ApplyAuthentication(client.Endpoint, client.ClientCredentials, _camera);

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
                    EventService.Close(client);
                }
            }
        }

        #endregion
    }
}
