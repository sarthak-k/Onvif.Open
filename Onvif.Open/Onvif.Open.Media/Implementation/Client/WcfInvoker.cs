// Sarthak Killedar
// 2026-11-07

using System;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;

namespace Onvif.Open.Media.Implementation.Client
{
    /// <summary>
    /// Runs one call on a freshly created WCF client and always closes (or aborts) it.
    /// </summary>
    internal static class WcfInvoker
    {
        #region Public Methods

        public static T Invoke<TClient, T>(Func<TClient> createClient, Func<TClient, T> call)
            where TClient : ICommunicationObject
        {
            var client = createClient();
            try
            {
                return call(client);
            }
            finally
            {
                Close(client);
            }
        }

        public static async Task<T> InvokeAsync<TClient, T>(Func<TClient> createClient, Func<TClient, Task<T>> call, CancellationToken cancellationToken)
            where TClient : ICommunicationObject
        {
            cancellationToken.ThrowIfCancellationRequested();

            var client = createClient();
            using (cancellationToken.Register(client.Abort))
            {
                try
                {
                    return await call(client).ConfigureAwait(false);
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
        }

        public static void Close(ICommunicationObject client)
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
