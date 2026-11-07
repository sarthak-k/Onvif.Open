// Sarthak Killedar
// 2026-11-07
//
// Hand written WCF contract for the ONVIF Recording Search service (search.wsdl, ver10) operations used by Onvif.Open.

using System;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Onvif.Open.Replay.Ver10
{
    internal static class ReplayNamespaces
    {
        public const string Schema = "http://www.onvif.org/ver10/schema";
        public const string Search = "http://www.onvif.org/ver10/search/wsdl";
        public const string Replay = "http://www.onvif.org/ver10/replay/wsdl";
    }

    [ServiceContract(Namespace = ReplayNamespaces.Search, Name = "SearchPort")]
    [XmlSerializerFormat(SupportFaults = true)]
    public interface ISearchPort
    {
        [OperationContract(Action = ReplayNamespaces.Search + "/GetRecordingSummary", ReplyAction = "*")]
        Task<GetRecordingSummaryResponse> GetRecordingSummaryAsync(GetRecordingSummaryRequest request);

        [OperationContract(Action = ReplayNamespaces.Search + "/GetRecordingInformation", ReplyAction = "*")]
        Task<GetRecordingInformationResponse> GetRecordingInformationAsync(GetRecordingInformationRequest request);

        [OperationContract(Action = ReplayNamespaces.Search + "/FindRecordings", ReplyAction = "*")]
        Task<FindRecordingsResponse> FindRecordingsAsync(FindRecordingsRequest request);

        [OperationContract(Action = ReplayNamespaces.Search + "/GetRecordingSearchResults", ReplyAction = "*")]
        Task<GetRecordingSearchResultsResponse> GetRecordingSearchResultsAsync(GetRecordingSearchResultsRequest request);

        [OperationContract(Action = ReplayNamespaces.Search + "/EndSearch", ReplyAction = "*")]
        Task<EndSearchResponse> EndSearchAsync(EndSearchRequest request);
    }

    [MessageContract(WrapperName = "GetRecordingSummary", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class GetRecordingSummaryRequest
    {
    }

    [MessageContract(WrapperName = "GetRecordingSummaryResponse", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class GetRecordingSummaryResponse
    {
        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 0)]
        public RecordingSummaryType Summary;
    }

    [MessageContract(WrapperName = "GetRecordingInformation", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class GetRecordingInformationRequest
    {
        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 0)]
        public string RecordingToken;
    }

    [MessageContract(WrapperName = "GetRecordingInformationResponse", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class GetRecordingInformationResponse
    {
        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 0)]
        public RecordingInformationType RecordingInformation;
    }

    [MessageContract(WrapperName = "FindRecordings", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class FindRecordingsRequest
    {
        /// <summary>Empty scope: all recordings.</summary>
        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 0)]
        public SearchScopeType Scope = new SearchScopeType();

        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 1)]
        public string KeepAliveTime;
    }

    [MessageContract(WrapperName = "FindRecordingsResponse", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class FindRecordingsResponse
    {
        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 0)]
        public string SearchToken;
    }

    [MessageContract(WrapperName = "GetRecordingSearchResults", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class GetRecordingSearchResultsRequest
    {
        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 0)]
        public string SearchToken;

        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 1)]
        public int MinResults;

        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 2)]
        public int MaxResults;

        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 3)]
        public string WaitTime;
    }

    [MessageContract(WrapperName = "GetRecordingSearchResultsResponse", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class GetRecordingSearchResultsResponse
    {
        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 0)]
        public FindRecordingResultListType ResultList;
    }

    [MessageContract(WrapperName = "EndSearch", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class EndSearchRequest
    {
        [MessageBodyMember(Namespace = ReplayNamespaces.Search, Order = 0)]
        public string SearchToken;
    }

    [MessageContract(WrapperName = "EndSearchResponse", WrapperNamespace = ReplayNamespaces.Search, IsWrapped = true)]
    public class EndSearchResponse
    {
    }

    [XmlType(Namespace = ReplayNamespaces.Schema)]
    public class SearchScopeType
    {
    }

    [XmlType(Namespace = ReplayNamespaces.Schema)]
    public class RecordingSummaryType
    {
        [XmlElement(Order = 0)]
        public DateTime DataFrom { get; set; }

        [XmlElement(Order = 1)]
        public DateTime DataUntil { get; set; }

        [XmlElement(Order = 2)]
        public int NumberRecordings { get; set; }
    }

    [XmlType(Namespace = ReplayNamespaces.Schema)]
    public class FindRecordingResultListType
    {
        /// <summary>Queued, Searching, Completed or Unknown.</summary>
        [XmlElement(Order = 0)]
        public string SearchState { get; set; }

        [XmlElement("RecordingInformation", Order = 1)]
        public RecordingInformationType[] RecordingInformation { get; set; }
    }

    [XmlType(Namespace = ReplayNamespaces.Schema)]
    public class RecordingInformationType
    {
        [XmlElement(Order = 0)]
        public string RecordingToken { get; set; }

        [XmlElement(Order = 1)]
        public RecordingSourceInformationType Source { get; set; }

        [XmlElement(Order = 2)]
        public DateTime EarliestRecording { get; set; }

        [XmlIgnore]
        public bool EarliestRecordingSpecified { get; set; }

        [XmlElement(Order = 3)]
        public DateTime LatestRecording { get; set; }

        [XmlIgnore]
        public bool LatestRecordingSpecified { get; set; }

        [XmlElement(Order = 4)]
        public string Content { get; set; }

        [XmlElement("Track", Order = 5)]
        public TrackInformationType[] Track { get; set; }

        /// <summary>Initiated, Recording, Stopped, Removing, Removed or Unknown.</summary>
        [XmlElement(Order = 6)]
        public string RecordingStatus { get; set; }
    }

    [XmlType(Namespace = ReplayNamespaces.Schema)]
    public class RecordingSourceInformationType
    {
        [XmlElement(Order = 0)]
        public string SourceId { get; set; }

        [XmlElement(Order = 1)]
        public string Name { get; set; }

        [XmlElement(Order = 2)]
        public string Location { get; set; }

        [XmlElement(Order = 3)]
        public string Description { get; set; }

        [XmlElement(Order = 4)]
        public string Address { get; set; }
    }

    [XmlType(Namespace = ReplayNamespaces.Schema)]
    public class TrackInformationType
    {
        [XmlElement(Order = 0)]
        public string TrackToken { get; set; }

        /// <summary>Video, Audio, Metadata or Extended.</summary>
        [XmlElement(Order = 1)]
        public string TrackType { get; set; }

        [XmlElement(Order = 2)]
        public string Description { get; set; }

        [XmlElement(Order = 3)]
        public DateTime DataFrom { get; set; }

        [XmlElement(Order = 4)]
        public DateTime DataTo { get; set; }
    }

    public class SearchClient : ClientBase<ISearchPort>
    {
        public SearchClient(System.ServiceModel.Channels.Binding binding, EndpointAddress remoteAddress)
            : base(binding, remoteAddress)
        {
        }

        public ISearchPort Port => Channel;
    }
}
