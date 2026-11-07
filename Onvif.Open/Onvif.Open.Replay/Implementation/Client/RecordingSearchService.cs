// Sarthak Killedar
// 2026-11-07

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using Onvif.Open.Core.Abstract.Interface.Camera;
using Onvif.Open.Core.Abstract.Interface.Replay;
using Onvif.Open.Core.Implementation;
using Onvif.Open.Core.Replay;
using Onvif.Open.Replay.Ver10;

namespace Onvif.Open.Replay.Implementation.Client
{
    /// <summary>
    /// Client for the ONVIF Recording Search (ver10) service.
    /// </summary>
    public sealed class RecordingSearchService : IRecordingSearchService
    {
        #region Constructor

        private RecordingSearchService(Uri serviceUri, ICameraInformation camera)
        {
            ServiceUri = serviceUri;
            _camera = camera;
        }

        #endregion

        #region Private Variables

        private const int PageSize = 100;
        private const int MaxPages = 1000;
        private const string KeepAlive = "PT30S";
        private const string WaitTime = "PT5S";

        private readonly ICameraInformation _camera;

        #endregion

        #region Public Variables

        public Uri ServiceUri { get; }

        #endregion

        #region Public Methods

        public async Task<DeviceRecordingSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
        {
            var response = await InvokeAsync(port => port.GetRecordingSummaryAsync(new GetRecordingSummaryRequest()), cancellationToken).ConfigureAwait(false);
            var summary = response?.Summary;

            return summary == null
                ? new DeviceRecordingSummary(null, null, 0)
                : new DeviceRecordingSummary(ToTime(summary.DataFrom), ToTime(summary.DataUntil), summary.NumberRecordings);
        }

        public async Task<IReadOnlyList<DeviceRecording>> FindRecordingsAsync(CancellationToken cancellationToken = default)
        {
            var start = await InvokeAsync(port => port.FindRecordingsAsync(new FindRecordingsRequest { KeepAliveTime = KeepAlive }), cancellationToken).ConfigureAwait(false);
            var token = start?.SearchToken ?? throw new InvalidOperationException("The device did not return a search token.");
            var recordings = new List<DeviceRecording>();

            try
            {
                // Results arrive in pages until the search is completed.
                for (var page = 0; page < MaxPages; page++)
                {
                    var request = new GetRecordingSearchResultsRequest { SearchToken = token, MinResults = 1, MaxResults = PageSize, WaitTime = WaitTime };
                    var results = (await InvokeAsync(port => port.GetRecordingSearchResultsAsync(request), cancellationToken).ConfigureAwait(false))?.ResultList;
                    var batch = results?.RecordingInformation ?? Array.Empty<RecordingInformationType>();

                    recordings.AddRange(batch.Where(r => !string.IsNullOrEmpty(r.RecordingToken)).Select(ToRecording));

                    if (results == null || string.Equals(results.SearchState, "Completed", StringComparison.OrdinalIgnoreCase) || batch.Length == 0)
                    {
                        break;
                    }
                }
            }
            finally
            {
                await EndSearchAsync(token).ConfigureAwait(false);
            }

            // Some devices report a recording again on a later page.
            return recordings.GroupBy(r => r.Token, StringComparer.Ordinal).Select(g => g.Last()).ToList();
        }

        public async Task<DeviceRecording> GetRecordingAsync(string recordingToken, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(recordingToken))
            {
                throw new ArgumentNullException(nameof(recordingToken));
            }

            try
            {
                var response = await InvokeAsync(port => port.GetRecordingInformationAsync(new GetRecordingInformationRequest { RecordingToken = recordingToken }), cancellationToken).ConfigureAwait(false);
                return response?.RecordingInformation == null ? null : ToRecording(response.RecordingInformation);
            }
            catch (FaultException ex) when (OnvifFaults.IsInvalidArgument(ex))
            {
                return null;
            }
        }

        internal static IRecordingSearchService CreateInstance(Uri serviceUri, ICameraInformation camera)
        {
            return new RecordingSearchService(serviceUri, camera);
        }

        internal static DeviceRecording ToRecording(RecordingInformationType information)
        {
            var tracks = (information.Track ?? Array.Empty<TrackInformationType>())
                .Where(t => t != null)
                .Select(t => new DeviceRecordingTrack(t.TrackToken, Parse(t.TrackType, DeviceTrackType.Unknown), t.Description, ToTime(t.DataFrom), ToTime(t.DataTo)))
                .ToList();

            return new DeviceRecording(
                information.RecordingToken,
                information.Source?.Name,
                information.Source?.SourceId,
                information.Source?.Location,
                information.Content,
                information.EarliestRecordingSpecified ? ToTime(information.EarliestRecording) : null,
                information.LatestRecordingSpecified ? ToTime(information.LatestRecording) : null,
                Parse(information.RecordingStatus, DeviceRecordingStatus.Unknown),
                tracks);
        }

        #endregion

        #region Private Methods

        private async Task EndSearchAsync(string token)
        {
            // Best effort: the search session times out on the device anyway (KeepAliveTime).
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await InvokeAsync(port => port.EndSearchAsync(new EndSearchRequest { SearchToken = token }), timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is CommunicationException || ex is TimeoutException || ex is OperationCanceledException)
            {
            }
        }

        private Task<T> InvokeAsync<T>(Func<ISearchPort, Task<T>> call, CancellationToken cancellationToken)
        {
            var client = new SearchClient(OnvifBindingFactory.CreateBinding(ServiceUri), new EndpointAddress(ServiceUri));
            return OnvifCall.InvokeAsync(client, client.Endpoint, client.ClientCredentials, _camera, () => call(client.Port), cancellationToken);
        }

        private static DateTimeOffset? ToTime(DateTime value)
        {
            if (value == default)
            {
                return null;
            }

            return new DateTimeOffset(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime());
        }

        private static TEnum Parse<TEnum>(string value, TEnum fallback)
            where TEnum : struct
        {
            return Enum.TryParse<TEnum>(value?.Trim(), true, out var parsed) ? parsed : fallback;
        }

        #endregion
    }
}
