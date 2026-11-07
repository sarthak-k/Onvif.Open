// Sarthak Killedar
// 2026-11-07

using System;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Implementation;

namespace Onvif.Open.Replay.Implementation.Client
{
    /// <summary>
    /// One authenticated SOAP call: applies WS-Security to a fresh client, honours cancellation and closes the client.
    /// </summary>
    internal static class OnvifCall
    {
        #region Public Methods

        public static async Task<T> InvokeAsync<T>(ICommunicationObject client, ServiceEndpoint endpoint, ClientCredentials credentials, ICameraInformation camera, Func<Task<T>> call, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var authenticator = PasswordDigestBuilder.GetBuilder().SetCamera(camera).Build();
            endpoint.EndpointBehaviors.Add(authenticator);
            authenticator.ConfigureCredentials(credentials);

            using (cancellationToken.Register(client.Abort))
            {
                try
                {
                    return await call().ConfigureAwait(false);
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
