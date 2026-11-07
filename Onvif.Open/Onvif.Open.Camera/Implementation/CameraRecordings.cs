// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Device;
using Onvif.Open.Core.Abstract.Interface.Replay;
using Onvif.Open.Core.Abstract.Interface.Streaming;
using Onvif.Open.Core.Operations;
using Onvif.Open.Core.Replay;
using Onvif.Open.Replay.Implementation.Builder;
using Onvif.Open.Streaming.Implementation.Builder;

namespace Onvif.Open.Camera.Implementation
{
    /// <summary>
    /// Recordings stored on the camera (ONVIF Profile G): Recording Search and Replay services, RTSP replay.
    /// </summary>
    public class CameraRecordings : ICameraRecordings
    {
        #region Constructor

        private CameraRecordings(ICameraInformation camera, IDeviceManagement deviceManagement, ClockSynchronizer clock)
        {
            _camera = camera;
            _clock = clock ?? new ClockSynchronizer(camera, deviceManagement);
            _search = new Lazy<IRecordingSearchService>(() =>
            {
                var uri = deviceManagement.FindServiceUri(OnvifServiceNamespaces.Search);
                return uri == null ? null : RecordingSearchServiceBuilder.GetBuilder().SetCamera(camera).SetServiceUri(uri).Build();
            }, LazyThreadSafetyMode.ExecutionAndPublication);
            _replay = new Lazy<IReplayService>(() =>
            {
                var uri = deviceManagement.FindServiceUri(OnvifServiceNamespaces.Replay);
                return uri == null ? null : ReplayServiceBuilder.GetBuilder().SetCamera(camera).SetServiceUri(uri).Build();
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        #endregion

        #region Private Variables

        private readonly ICameraInformation _camera;
        private readonly ClockSynchronizer _clock;
        private readonly Lazy<IRecordingSearchService> _search;
        private readonly Lazy<IReplayService> _replay;

        #endregion

        #region Public Methods

        public Task<DeviceRecordingSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
        {
            return _clock.RunAsync(async ct => await (await SearchAsync(ct).ConfigureAwait(false)).GetSummaryAsync(ct).ConfigureAwait(false), cancellationToken);
        }

        public Task<IReadOnlyList<DeviceRecording>> GetRecordingsAsync(CancellationToken cancellationToken = default)
        {
            return _clock.RunAsync(async ct => await (await SearchAsync(ct).ConfigureAwait(false)).FindRecordingsAsync(ct).ConfigureAwait(false), cancellationToken);
        }

        public async Task<ILiveVideoStream> StartReplayAsync(string recordingToken, DateTimeOffset from, ReplayOptions options = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(recordingToken))
            {
                throw new ArgumentNullException(nameof(recordingToken));
            }

            options = (options ?? new ReplayOptions()).Validate(from);

            var uri = await _clock.RunAsync(async ct =>
            {
                var replay = _replay.IsValueCreated ? _replay.Value : await Task.Run(() => _replay.Value, ct).ConfigureAwait(false);
                if (replay == null)
                {
                    throw new NotSupportedException("The camera does not have a Replay service (ONVIF Profile G).");
                }

                return await replay.GetReplayUriAsync(recordingToken, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            // Times are the device's recording times (as in DeviceRecording and VideoFrame.CaptureTime),
            // so no clock correction applies.
            return await LiveVideoStreamBuilder.GetBuilder()
                .SetStreamUri(uri)
                .SetCamera(_camera)
                .SetReplay(from, options)
                .BuildAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        internal static ICameraRecordings CreateInstance(ICameraInformation camera, IDeviceManagement deviceManagement, ClockSynchronizer clock = null)
        {
            return new CameraRecordings(camera, deviceManagement, clock);
        }

        #endregion

        #region Private Methods

        private async Task<IRecordingSearchService> SearchAsync(CancellationToken cancellationToken)
        {
            var search = _search.IsValueCreated ? _search.Value : await Task.Run(() => _search.Value, cancellationToken).ConfigureAwait(false);
            return search ?? throw new NotSupportedException("The camera does not have a Recording Search service (ONVIF Profile G).");
        }

        #endregion
    }
}
