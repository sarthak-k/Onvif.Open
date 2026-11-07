// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Onvif.Open.Recording.Implementation.Recorder
{
    /// <summary>
    /// Reads an endless source of items (camera events, analytics frames) while a recorder runs,
    /// restarting it after failures.
    /// </summary>
    internal static class SourceMonitor
    {
        #region Private Variables

        private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(5);

        #endregion

        #region Public Methods

        public static async Task RunAsync<T>(Func<CancellationToken, IAsyncEnumerable<T>> source, Action<T> handle, string cameraId, string what, TimeProvider timeProvider, ILogger logger, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await foreach (var item in source(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                    {
                        handle(item);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (NotSupportedException ex)
                {
                    // The camera cannot provide it (e.g. no metadata configuration): retrying will not help.
                    logger.LogWarning("Camera {CameraId}: {What} not available: {Reason}", cameraId, what, ex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Camera {CameraId}: {What} failed, restarting", cameraId, what);
                }

                try
                {
                    await Task.Delay(RestartDelay, timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        #endregion
    }
}
