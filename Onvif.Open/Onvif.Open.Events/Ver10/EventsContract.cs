// Sarthak Killedar
// 2026-11-07
// Hand written WCF contract for the PullPoint operations of the ONVIF Events service (event.wsdl, ver10)
// and the WS-BaseNotification subscription manager. Responses keep the device's XML (XmlElement) and are
// parsed by local names, because devices differ in namespaces and extensions.

using System.ServiceModel;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;

namespace Onvif.Open.Events.Ver10
{
    internal static class EventNamespaces
    {
        public const string Events = "http://www.onvif.org/ver10/events/wsdl";
        public const string Notification = "http://docs.oasis-open.org/wsn/b-2";
        public const string Addressing = "http://www.w3.org/2005/08/addressing";
    }

    [ServiceContract(Namespace = EventNamespaces.Events, Name = "EventPortType")]
    [XmlSerializerFormat(SupportFaults = true)]
    public interface IEventPort
    {
        [OperationContract(Action = EventNamespaces.Events + "/EventPortType/CreatePullPointSubscriptionRequest", ReplyAction = "*")]
        Task<CreatePullPointSubscriptionResponse> CreatePullPointSubscriptionAsync(CreatePullPointSubscriptionRequest request);
    }

    [ServiceContract(Namespace = EventNamespaces.Events, Name = "PullPointSubscription")]
    [XmlSerializerFormat(SupportFaults = true)]
    public interface IPullPointSubscriptionPort
    {
        [OperationContract(Action = EventNamespaces.Events + "/PullPointSubscription/PullMessagesRequest", ReplyAction = "*")]
        Task<PullMessagesResponse> PullMessagesAsync(PullMessagesRequest request);

        [OperationContract(Action = "http://docs.oasis-open.org/wsn/bw-2/SubscriptionManager/RenewRequest", ReplyAction = "*")]
        Task<RenewResponse> RenewAsync(RenewRequest request);

        [OperationContract(Action = "http://docs.oasis-open.org/wsn/bw-2/SubscriptionManager/UnsubscribeRequest", ReplyAction = "*")]
        Task<UnsubscribeResponse> UnsubscribeAsync(UnsubscribeRequest request);
    }

    [MessageContract(WrapperName = "CreatePullPointSubscription", WrapperNamespace = EventNamespaces.Events, IsWrapped = true)]
    public class CreatePullPointSubscriptionRequest
    {
        /// <summary>xs:duration, e.g. "PT60S".</summary>
        [MessageBodyMember(Namespace = EventNamespaces.Events, Order = 0)]
        public string InitialTerminationTime;
    }

    [MessageContract(WrapperName = "CreatePullPointSubscriptionResponse", WrapperNamespace = EventNamespaces.Events, IsWrapped = true)]
    public class CreatePullPointSubscriptionResponse
    {
        [MessageBodyMember(Namespace = EventNamespaces.Events, Order = 0)]
        public EndpointReference SubscriptionReference;
    }

    /// <summary>
    /// A wsa:EndpointReference whose children (Address, ReferenceParameters, Metadata) are kept as XML,
    /// whatever WS-Addressing namespace version the device uses.
    /// </summary>
    [XmlType(Namespace = EventNamespaces.Events)]
    public class EndpointReference
    {
        [XmlAnyElement]
        public XmlElement[] Elements;
    }

    [MessageContract(WrapperName = "PullMessages", WrapperNamespace = EventNamespaces.Events, IsWrapped = true)]
    public class PullMessagesRequest
    {
        [MessageBodyMember(Namespace = EventNamespaces.Events, Order = 0)]
        public string Timeout;

        [MessageBodyMember(Namespace = EventNamespaces.Events, Order = 1)]
        public int MessageLimit;
    }

    [MessageContract(WrapperName = "PullMessagesResponse", WrapperNamespace = EventNamespaces.Events, IsWrapped = true)]
    public class PullMessagesResponse
    {
        [MessageBodyMember(Namespace = EventNamespaces.Notification, Order = 0)]
        [XmlAnyElement]
        public XmlElement[] Elements;
    }

    [MessageContract(WrapperName = "Renew", WrapperNamespace = EventNamespaces.Notification, IsWrapped = true)]
    public class RenewRequest
    {
        [MessageBodyMember(Namespace = EventNamespaces.Notification, Order = 0)]
        public string TerminationTime;
    }

    [MessageContract(WrapperName = "RenewResponse", WrapperNamespace = EventNamespaces.Notification, IsWrapped = true)]
    public class RenewResponse
    {
        [MessageBodyMember(Namespace = EventNamespaces.Notification, Order = 0)]
        [XmlAnyElement]
        public XmlElement[] Elements;
    }

    [MessageContract(WrapperName = "Unsubscribe", WrapperNamespace = EventNamespaces.Notification, IsWrapped = true)]
    public class UnsubscribeRequest
    {
    }

    [MessageContract(WrapperName = "UnsubscribeResponse", WrapperNamespace = EventNamespaces.Notification, IsWrapped = true)]
    public class UnsubscribeResponse
    {
        [MessageBodyMember(Namespace = EventNamespaces.Notification, Order = 0)]
        [XmlAnyElement]
        public XmlElement[] Elements;
    }

    public class EventClient : ClientBase<IEventPort>
    {
        public EventClient(System.ServiceModel.Channels.Binding binding, EndpointAddress remoteAddress)
            : base(binding, remoteAddress)
        {
        }

        public IEventPort Port => Channel;
    }

    public class PullPointSubscriptionClient : ClientBase<IPullPointSubscriptionPort>
    {
        public PullPointSubscriptionClient(System.ServiceModel.Channels.Binding binding, EndpointAddress remoteAddress)
            : base(binding, remoteAddress)
        {
        }

        public IPullPointSubscriptionPort Port => Channel;
    }
}
