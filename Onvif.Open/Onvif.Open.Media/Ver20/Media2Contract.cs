// Sarthak Killedar
// 2026-11-07
// Hand written WCF contract for the Media2 (media2.wsdl, ver20) operations used for live streaming.

using System.ServiceModel;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Onvif.Open.Media.Ver20
{
    [ServiceContract(Namespace = Media2Namespaces.Media2, Name = "Media2")]
    [XmlSerializerFormat(SupportFaults = true)]
    public interface IMedia2Port
    {
        [OperationContract(Action = Media2Namespaces.Media2 + "/GetProfiles", ReplyAction = "*")]
        GetProfiles2Response GetProfiles(GetProfiles2Request request);

        [OperationContract(Action = Media2Namespaces.Media2 + "/GetProfiles", ReplyAction = "*")]
        Task<GetProfiles2Response> GetProfilesAsync(GetProfiles2Request request);

        [OperationContract(Action = Media2Namespaces.Media2 + "/GetStreamUri", ReplyAction = "*")]
        GetStreamUri2Response GetStreamUri(GetStreamUri2Request request);

        [OperationContract(Action = Media2Namespaces.Media2 + "/GetStreamUri", ReplyAction = "*")]
        Task<GetStreamUri2Response> GetStreamUriAsync(GetStreamUri2Request request);

        [OperationContract(Action = Media2Namespaces.Media2 + "/GetSnapshotUri", ReplyAction = "*")]
        GetSnapshotUri2Response GetSnapshotUri(GetSnapshotUri2Request request);

        [OperationContract(Action = Media2Namespaces.Media2 + "/GetSnapshotUri", ReplyAction = "*")]
        Task<GetSnapshotUri2Response> GetSnapshotUriAsync(GetSnapshotUri2Request request);
    }

    [MessageContract(WrapperName = "GetProfiles", WrapperNamespace = Media2Namespaces.Media2, IsWrapped = true)]
    public class GetProfiles2Request
    {
        /// <summary>Configuration types to include: "All" returns every configuration of each profile.</summary>
        [MessageBodyMember(Namespace = Media2Namespaces.Media2, Order = 0)]
        [XmlElement("Type")]
        public string[] Type;
    }

    [MessageContract(WrapperName = "GetProfilesResponse", WrapperNamespace = Media2Namespaces.Media2, IsWrapped = true)]
    public class GetProfiles2Response
    {
        [MessageBodyMember(Namespace = Media2Namespaces.Media2, Order = 0)]
        [XmlElement("Profiles")]
        public MediaProfile2[] Profiles;
    }

    [MessageContract(WrapperName = "GetStreamUri", WrapperNamespace = Media2Namespaces.Media2, IsWrapped = true)]
    public class GetStreamUri2Request
    {
        /// <summary>"RtspUnicast" (RTP/UDP), "RTSP" (RTP over RTSP/TCP), "RtspOverHttp", ...</summary>
        [MessageBodyMember(Namespace = Media2Namespaces.Media2, Order = 0)]
        public string Protocol;

        [MessageBodyMember(Namespace = Media2Namespaces.Media2, Order = 1)]
        public string ProfileToken;
    }

    [MessageContract(WrapperName = "GetStreamUriResponse", WrapperNamespace = Media2Namespaces.Media2, IsWrapped = true)]
    public class GetStreamUri2Response
    {
        [MessageBodyMember(Namespace = Media2Namespaces.Media2, Order = 0)]
        public string Uri;
    }

    [MessageContract(WrapperName = "GetSnapshotUri", WrapperNamespace = Media2Namespaces.Media2, IsWrapped = true)]
    public class GetSnapshotUri2Request
    {
        [MessageBodyMember(Namespace = Media2Namespaces.Media2, Order = 0)]
        public string ProfileToken;
    }

    [MessageContract(WrapperName = "GetSnapshotUriResponse", WrapperNamespace = Media2Namespaces.Media2, IsWrapped = true)]
    public class GetSnapshotUri2Response
    {
        [MessageBodyMember(Namespace = Media2Namespaces.Media2, Order = 0)]
        public string Uri;
    }

    public class Media2Client : ClientBase<IMedia2Port>
    {
        public Media2Client(System.ServiceModel.Channels.Binding binding, EndpointAddress remoteAddress)
            : base(binding, remoteAddress)
        {
        }

        public IMedia2Port Port => Channel;
    }
}
