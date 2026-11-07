// Sarthak Killedar
// 2026-11-07
// Hand written WCF contract for the ONVIF DeviceIO service (deviceio.wsdl, ver10) operations used by Onvif.Open.

using System.ServiceModel;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace Onvif.Open.DeviceIo.Ver10
{
    internal static class DeviceIoNamespaces
    {
        public const string Schema = "http://www.onvif.org/ver10/schema";
        public const string DeviceIo = "http://www.onvif.org/ver10/deviceIO/wsdl";
    }

    [ServiceContract(Namespace = DeviceIoNamespaces.DeviceIo, Name = "DeviceIOPort")]
    [XmlSerializerFormat(SupportFaults = true)]
    public interface IDeviceIoPort
    {
        [OperationContract(Action = DeviceIoNamespaces.DeviceIo + "/GetDigitalInputs", ReplyAction = "*")]
        Task<GetDigitalInputsResponse> GetDigitalInputsAsync(GetDigitalInputsRequest request);
    }

    [MessageContract(WrapperName = "GetDigitalInputs", WrapperNamespace = DeviceIoNamespaces.DeviceIo, IsWrapped = true)]
    public class GetDigitalInputsRequest
    {
    }

    [MessageContract(WrapperName = "GetDigitalInputsResponse", WrapperNamespace = DeviceIoNamespaces.DeviceIo, IsWrapped = true)]
    public class GetDigitalInputsResponse
    {
        [MessageBodyMember(Namespace = DeviceIoNamespaces.DeviceIo, Order = 0)]
        [XmlElement("DigitalInputs")]
        public DigitalInputType[] DigitalInputs;
    }

    [XmlType(Namespace = DeviceIoNamespaces.Schema)]
    public class DigitalInputType
    {
        [XmlAttribute("token")]
        public string Token { get; set; }

        /// <summary>"closed" or "open".</summary>
        [XmlAttribute("IdleState")]
        public string IdleState { get; set; }
    }

    public class DeviceIoClient : ClientBase<IDeviceIoPort>
    {
        public DeviceIoClient(System.ServiceModel.Channels.Binding binding, EndpointAddress remoteAddress)
            : base(binding, remoteAddress)
        {
        }

        public IDeviceIoPort Port => Channel;
    }
}
