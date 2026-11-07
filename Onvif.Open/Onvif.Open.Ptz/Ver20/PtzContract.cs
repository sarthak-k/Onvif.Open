// Sarthak Killedar
// 2026-11-07
// Hand written WCF contract for the ONVIF PTZ service (ptz.wsdl, ver20) operations used by Onvif.Open.
// Optional members left null are omitted from the request.

using System.ServiceModel;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Onvif.Open.Ptz.Ver20
{
    [ServiceContract(Namespace = PtzNamespaces.Ptz, Name = "PTZ")]
    [XmlSerializerFormat(SupportFaults = true)]
    public interface IPtzPort
    {
        [OperationContract(Action = PtzNamespaces.Ptz + "/ContinuousMove", ReplyAction = "*")]
        Task<ContinuousMoveResponse> ContinuousMoveAsync(ContinuousMoveRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/RelativeMove", ReplyAction = "*")]
        Task<RelativeMoveResponse> RelativeMoveAsync(RelativeMoveRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/AbsoluteMove", ReplyAction = "*")]
        Task<AbsoluteMoveResponse> AbsoluteMoveAsync(AbsoluteMoveRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/Stop", ReplyAction = "*")]
        Task<StopResponse> StopAsync(StopRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/GetStatus", ReplyAction = "*")]
        Task<GetStatusResponse> GetStatusAsync(GetStatusRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/GetPresets", ReplyAction = "*")]
        Task<GetPresetsResponse> GetPresetsAsync(GetPresetsRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/GotoPreset", ReplyAction = "*")]
        Task<GotoPresetResponse> GotoPresetAsync(GotoPresetRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/SetPreset", ReplyAction = "*")]
        Task<SetPresetResponse> SetPresetAsync(SetPresetRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/RemovePreset", ReplyAction = "*")]
        Task<RemovePresetResponse> RemovePresetAsync(RemovePresetRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/GotoHomePosition", ReplyAction = "*")]
        Task<GotoHomePositionResponse> GotoHomePositionAsync(GotoHomePositionRequest request);

        [OperationContract(Action = PtzNamespaces.Ptz + "/SetHomePosition", ReplyAction = "*")]
        Task<SetHomePositionResponse> SetHomePositionAsync(SetHomePositionRequest request);
    }

    [MessageContract(WrapperName = "ContinuousMove", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class ContinuousMoveRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 1)]
        public PtzVectorType Velocity;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 2)]
        public string Timeout;
    }

    [MessageContract(WrapperName = "ContinuousMoveResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class ContinuousMoveResponse
    {
    }

    [MessageContract(WrapperName = "RelativeMove", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class RelativeMoveRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 1)]
        public PtzVectorType Translation;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 2)]
        public PtzVectorType Speed;
    }

    [MessageContract(WrapperName = "RelativeMoveResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class RelativeMoveResponse
    {
    }

    [MessageContract(WrapperName = "AbsoluteMove", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class AbsoluteMoveRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 1)]
        public PtzVectorType Position;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 2)]
        public PtzVectorType Speed;
    }

    [MessageContract(WrapperName = "AbsoluteMoveResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class AbsoluteMoveResponse
    {
    }

    [MessageContract(WrapperName = "Stop", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class StopRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 1)]
        public bool PanTilt;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 2)]
        public bool Zoom;
    }

    [MessageContract(WrapperName = "StopResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class StopResponse
    {
    }

    [MessageContract(WrapperName = "GetStatus", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class GetStatusRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;
    }

    [MessageContract(WrapperName = "GetStatusResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class GetStatusResponse
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public PtzStatusType PTZStatus;
    }

    [MessageContract(WrapperName = "GetPresets", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class GetPresetsRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;
    }

    [MessageContract(WrapperName = "GetPresetsResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class GetPresetsResponse
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        [XmlElement("Preset")]
        public PtzPresetType[] Preset;
    }

    [MessageContract(WrapperName = "GotoPreset", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class GotoPresetRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 1)]
        public string PresetToken;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 2)]
        public PtzVectorType Speed;
    }

    [MessageContract(WrapperName = "GotoPresetResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class GotoPresetResponse
    {
    }

    [MessageContract(WrapperName = "SetPreset", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class SetPresetRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 1)]
        public string PresetName;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 2)]
        public string PresetToken;
    }

    [MessageContract(WrapperName = "SetPresetResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class SetPresetResponse
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string PresetToken;
    }

    [MessageContract(WrapperName = "RemovePreset", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class RemovePresetRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 1)]
        public string PresetToken;
    }

    [MessageContract(WrapperName = "RemovePresetResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class RemovePresetResponse
    {
    }

    [MessageContract(WrapperName = "GotoHomePosition", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class GotoHomePositionRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;

        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 1)]
        public PtzVectorType Speed;
    }

    [MessageContract(WrapperName = "GotoHomePositionResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class GotoHomePositionResponse
    {
    }

    [MessageContract(WrapperName = "SetHomePosition", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class SetHomePositionRequest
    {
        [MessageBodyMember(Namespace = PtzNamespaces.Ptz, Order = 0)]
        public string ProfileToken;
    }

    [MessageContract(WrapperName = "SetHomePositionResponse", WrapperNamespace = PtzNamespaces.Ptz, IsWrapped = true)]
    public class SetHomePositionResponse
    {
    }

    public class PtzClient : ClientBase<IPtzPort>
    {
        public PtzClient(System.ServiceModel.Channels.Binding binding, EndpointAddress remoteAddress)
            : base(binding, remoteAddress)
        {
        }

        public IPtzPort Port => Channel;
    }
}
