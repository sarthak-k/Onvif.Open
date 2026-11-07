// Sarthak Killedar
// 2026-11-07
// Hand written WCF contract for the ONVIF Imaging service (imaging.wsdl, ver20).

using System.ServiceModel;
using System.Threading.Tasks;

namespace Onvif.Open.Imaging.Ver20
{
    [ServiceContract(Namespace = ImagingNamespaces.Imaging, Name = "ImagingPort")]
    [XmlSerializerFormat(SupportFaults = true)]
    public interface IImagingPort
    {
        [OperationContract(Action = ImagingNamespaces.Imaging + "/GetImagingSettings", ReplyAction = "*")]
        Task<GetImagingSettingsResponse> GetImagingSettingsAsync(GetImagingSettingsRequest request);

        [OperationContract(Action = ImagingNamespaces.Imaging + "/SetImagingSettings", ReplyAction = "*")]
        Task<SetImagingSettingsResponse> SetImagingSettingsAsync(SetImagingSettingsRequest request);

        [OperationContract(Action = ImagingNamespaces.Imaging + "/GetOptions", ReplyAction = "*")]
        Task<GetOptionsResponse> GetOptionsAsync(GetOptionsRequest request);

        [OperationContract(Action = ImagingNamespaces.Imaging + "/Move", ReplyAction = "*")]
        Task<MoveResponse> MoveAsync(MoveRequest request);

        [OperationContract(Action = ImagingNamespaces.Imaging + "/Stop", ReplyAction = "*")]
        Task<StopResponse> StopAsync(StopRequest request);

        [OperationContract(Action = ImagingNamespaces.Imaging + "/GetStatus", ReplyAction = "*")]
        Task<GetStatusResponse> GetStatusAsync(GetStatusRequest request);
    }

    [MessageContract(WrapperName = "GetImagingSettings", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class GetImagingSettingsRequest
    {
        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 0)]
        public string VideoSourceToken;
    }

    [MessageContract(WrapperName = "GetImagingSettingsResponse", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class GetImagingSettingsResponse
    {
        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 0)]
        public ImagingSettings20 ImagingSettings;
    }

    [MessageContract(WrapperName = "SetImagingSettings", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class SetImagingSettingsRequest
    {
        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 0)]
        public string VideoSourceToken;

        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 1)]
        public ImagingSettings20 ImagingSettings;

        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 2)]
        public bool ForcePersistence;
    }

    [MessageContract(WrapperName = "SetImagingSettingsResponse", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class SetImagingSettingsResponse
    {
    }

    [MessageContract(WrapperName = "GetOptions", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class GetOptionsRequest
    {
        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 0)]
        public string VideoSourceToken;
    }

    [MessageContract(WrapperName = "GetOptionsResponse", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class GetOptionsResponse
    {
        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 0)]
        public ImagingOptions20 ImagingOptions;
    }

    [MessageContract(WrapperName = "Move", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class MoveRequest
    {
        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 0)]
        public string VideoSourceToken;

        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 1)]
        public FocusMove Focus;
    }

    [MessageContract(WrapperName = "MoveResponse", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class MoveResponse
    {
    }

    [MessageContract(WrapperName = "Stop", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class StopRequest
    {
        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 0)]
        public string VideoSourceToken;
    }

    [MessageContract(WrapperName = "StopResponse", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class StopResponse
    {
    }

    [MessageContract(WrapperName = "GetStatus", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class GetStatusRequest
    {
        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 0)]
        public string VideoSourceToken;
    }

    [MessageContract(WrapperName = "GetStatusResponse", WrapperNamespace = ImagingNamespaces.Imaging, IsWrapped = true)]
    public class GetStatusResponse
    {
        [MessageBodyMember(Namespace = ImagingNamespaces.Imaging, Order = 0)]
        public ImagingStatus20 Status;
    }

    public class ImagingClient : ClientBase<IImagingPort>
    {
        public ImagingClient(System.ServiceModel.Channels.Binding binding, EndpointAddress remoteAddress)
            : base(binding, remoteAddress)
        {
        }

        public IImagingPort Port => Channel;
    }
}
