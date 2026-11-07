// Sarthak Killedar
// Hand written WCF contract for the operations of the ONVIF Media service (media.wsdl, ver10)
// used for live streaming. Kept intentionally small instead of generating the whole WSDL.

using System.ServiceModel;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Onvif.Open.Media.Ver10
{
    [ServiceContract(Namespace = OnvifNamespaces.Media, Name = "Media")]
    [XmlSerializerFormat(SupportFaults = true)]
    public interface IMediaPort
    {
        [OperationContract(Action = OnvifNamespaces.Media + "/GetProfiles", ReplyAction = "*")]
        GetProfilesResponse GetProfiles(GetProfilesRequest request);

        [OperationContract(Action = OnvifNamespaces.Media + "/GetProfiles", ReplyAction = "*")]
        Task<GetProfilesResponse> GetProfilesAsync(GetProfilesRequest request);

        [OperationContract(Action = OnvifNamespaces.Media + "/GetStreamUri", ReplyAction = "*")]
        GetStreamUriResponse GetStreamUri(GetStreamUriRequest request);

        [OperationContract(Action = OnvifNamespaces.Media + "/GetStreamUri", ReplyAction = "*")]
        Task<GetStreamUriResponse> GetStreamUriAsync(GetStreamUriRequest request);

        [OperationContract(Action = OnvifNamespaces.Media + "/GetSnapshotUri", ReplyAction = "*")]
        GetSnapshotUriResponse GetSnapshotUri(GetSnapshotUriRequest request);

        [OperationContract(Action = OnvifNamespaces.Media + "/GetSnapshotUri", ReplyAction = "*")]
        Task<GetSnapshotUriResponse> GetSnapshotUriAsync(GetSnapshotUriRequest request);
    }

    [MessageContract(WrapperName = "GetProfiles", WrapperNamespace = OnvifNamespaces.Media, IsWrapped = true)]
    public class GetProfilesRequest
    {
    }

    [MessageContract(WrapperName = "GetProfilesResponse", WrapperNamespace = OnvifNamespaces.Media, IsWrapped = true)]
    public class GetProfilesResponse
    {
        [MessageBodyMember(Namespace = OnvifNamespaces.Media, Order = 0)]
        [XmlElement("Profiles")]
        public Profile[] Profiles;
    }

    [MessageContract(WrapperName = "GetStreamUri", WrapperNamespace = OnvifNamespaces.Media, IsWrapped = true)]
    public class GetStreamUriRequest
    {
        [MessageBodyMember(Namespace = OnvifNamespaces.Media, Order = 0)]
        public StreamSetup StreamSetup;

        [MessageBodyMember(Namespace = OnvifNamespaces.Media, Order = 1)]
        public string ProfileToken;
    }

    [MessageContract(WrapperName = "GetStreamUriResponse", WrapperNamespace = OnvifNamespaces.Media, IsWrapped = true)]
    public class GetStreamUriResponse
    {
        [MessageBodyMember(Namespace = OnvifNamespaces.Media, Order = 0)]
        public MediaUri MediaUri;
    }

    [MessageContract(WrapperName = "GetSnapshotUri", WrapperNamespace = OnvifNamespaces.Media, IsWrapped = true)]
    public class GetSnapshotUriRequest
    {
        [MessageBodyMember(Namespace = OnvifNamespaces.Media, Order = 0)]
        public string ProfileToken;
    }

    [MessageContract(WrapperName = "GetSnapshotUriResponse", WrapperNamespace = OnvifNamespaces.Media, IsWrapped = true)]
    public class GetSnapshotUriResponse
    {
        [MessageBodyMember(Namespace = OnvifNamespaces.Media, Order = 0)]
        public MediaUri MediaUri;
    }

    public class MediaClient : ClientBase<IMediaPort>
    {
        public MediaClient(System.ServiceModel.Channels.Binding binding, EndpointAddress remoteAddress)
            : base(binding, remoteAddress)
        {
        }

        public IMediaPort Port => Channel;
    }
}
