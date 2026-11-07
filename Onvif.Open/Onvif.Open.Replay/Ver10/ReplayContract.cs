// Sarthak Killedar
// 2026-11-07
//
// Hand written WCF contract for the ONVIF Replay service (replay.wsdl, ver10) operations used by Onvif.Open.

using System.ServiceModel;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Onvif.Open.Replay.Ver10
{
    [ServiceContract(Namespace = ReplayNamespaces.Replay, Name = "ReplayPort")]
    [XmlSerializerFormat(SupportFaults = true)]
    public interface IReplayPort
    {
        [OperationContract(Action = ReplayNamespaces.Replay + "/GetReplayUri", ReplyAction = "*")]
        Task<GetReplayUriResponse> GetReplayUriAsync(GetReplayUriRequest request);
    }

    [MessageContract(WrapperName = "GetReplayUri", WrapperNamespace = ReplayNamespaces.Replay, IsWrapped = true)]
    public class GetReplayUriRequest
    {
        [MessageBodyMember(Namespace = ReplayNamespaces.Replay, Order = 0)]
        public StreamSetupType StreamSetup = new StreamSetupType();

        [MessageBodyMember(Namespace = ReplayNamespaces.Replay, Order = 1)]
        public string RecordingToken;
    }

    [MessageContract(WrapperName = "GetReplayUriResponse", WrapperNamespace = ReplayNamespaces.Replay, IsWrapped = true)]
    public class GetReplayUriResponse
    {
        [MessageBodyMember(Namespace = ReplayNamespaces.Replay, Order = 0)]
        public string Uri;
    }

    /// <summary>Replay is RTP unicast over RTSP (ONVIF Streaming Specification).</summary>
    [XmlType(Namespace = ReplayNamespaces.Schema)]
    public class StreamSetupType
    {
        [XmlElement(Order = 0)]
        public string Stream { get; set; } = "RTP-Unicast";

        [XmlElement(Order = 1)]
        public TransportType Transport { get; set; } = new TransportType();
    }

    [XmlType(Namespace = ReplayNamespaces.Schema)]
    public class TransportType
    {
        [XmlElement(Order = 0)]
        public string Protocol { get; set; } = "RTSP";
    }

    public class ReplayClient : ClientBase<IReplayPort>
    {
        public ReplayClient(System.ServiceModel.Channels.Binding binding, EndpointAddress remoteAddress)
            : base(binding, remoteAddress)
        {
        }

        public IReplayPort Port => Channel;
    }
}
