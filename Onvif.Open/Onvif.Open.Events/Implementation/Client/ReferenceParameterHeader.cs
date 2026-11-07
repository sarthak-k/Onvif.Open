// Sarthak Killedar
// 2026-11-07

using System.ServiceModel.Channels;
using System.Xml;

namespace Onvif.Open.Events.Implementation.Client
{
    /// <summary>
    /// A WS-Addressing reference parameter of a subscription endpoint reference, echoed verbatim as a SOAP
    /// header on every request to the subscription (some devices identify subscriptions this way).
    /// </summary>
    internal sealed class ReferenceParameterHeader : AddressHeader
    {
        #region Constructor

        public ReferenceParameterHeader(XmlElement element)
        {
            _element = element;
        }

        #endregion

        #region Private Variables

        private readonly XmlElement _element;

        #endregion

        #region Public Variables

        public override string Name => _element.LocalName;
        public override string Namespace => _element.NamespaceURI;

        #endregion

        #region Protected Methods

        protected override void OnWriteStartAddressHeader(XmlDictionaryWriter writer)
        {
            writer.WriteStartElement(_element.Prefix, _element.LocalName, _element.NamespaceURI);

            foreach (XmlAttribute attribute in _element.Attributes)
            {
                attribute.WriteTo(writer);
            }
        }

        protected override void OnWriteAddressHeaderContents(XmlDictionaryWriter writer)
        {
            foreach (XmlNode child in _element.ChildNodes)
            {
                child.WriteTo(writer);
            }
        }

        #endregion
    }
}
