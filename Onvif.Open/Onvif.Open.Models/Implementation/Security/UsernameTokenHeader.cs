// Sarthak Killedar
// WS-Security UsernameToken (PasswordDigest) header, as required by the ONVIF Core Specification.
// Replaces the .NET Framework only Microsoft.Web.Services3 (WSE 3.0) dependency.

using System;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.ServiceModel.Channels;

namespace Onvif.Open.Core.Implementation.Security
{
    /// <summary>
    /// SOAP header carrying a WS-Security UsernameToken with a hashed (digest) password:
    /// <c>PasswordDigest = Base64(SHA1(nonce + created + password))</c>.
    /// </summary>
    public sealed class UsernameTokenHeader : MessageHeader
    {
        public const string WsseNamespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
        public const string WsuNamespace = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        public const string PasswordDigestType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest";
        public const string Base64EncodingType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";

        public UsernameTokenHeader(string username, string password)
            : this(username, password, RandomNumberGenerator.GetBytes(16), DateTime.UtcNow)
        {
        }

        public UsernameTokenHeader(string username, string password, byte[] nonce, DateTime createdUtc)
        {
            Username = username ?? throw new ArgumentNullException(nameof(username));
            Nonce = nonce ?? throw new ArgumentNullException(nameof(nonce));
            Created = createdUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
            PasswordDigest = ComputePasswordDigest(Nonce, Created, password ?? string.Empty);
        }

        public string Username { get; }
        public byte[] Nonce { get; }
        public string Created { get; }
        public string PasswordDigest { get; }

        public override string Name => "Security";
        public override string Namespace => WsseNamespace;
        public override bool MustUnderstand => false;

        public static string ComputePasswordDigest(byte[] nonce, string created, string password)
        {
            var createdBytes = Encoding.UTF8.GetBytes(created);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var buffer = new byte[nonce.Length + createdBytes.Length + passwordBytes.Length];
            Buffer.BlockCopy(nonce, 0, buffer, 0, nonce.Length);
            Buffer.BlockCopy(createdBytes, 0, buffer, nonce.Length, createdBytes.Length);
            Buffer.BlockCopy(passwordBytes, 0, buffer, nonce.Length + createdBytes.Length, passwordBytes.Length);
            return Convert.ToBase64String(SHA1.HashData(buffer));
        }

        protected override void OnWriteStartHeader(XmlDictionaryWriter writer, MessageVersion messageVersion)
        {
            writer.WriteStartElement("wsse", Name, Namespace);
            writer.WriteXmlnsAttribute("wsu", WsuNamespace);
        }

        protected override void OnWriteHeaderContents(XmlDictionaryWriter writer, MessageVersion messageVersion)
        {
            writer.WriteStartElement("wsse", "UsernameToken", WsseNamespace);

            writer.WriteElementString("wsse", "Username", WsseNamespace, Username);

            writer.WriteStartElement("wsse", "Password", WsseNamespace);
            writer.WriteAttributeString("Type", PasswordDigestType);
            writer.WriteString(PasswordDigest);
            writer.WriteEndElement();

            writer.WriteStartElement("wsse", "Nonce", WsseNamespace);
            writer.WriteAttributeString("EncodingType", Base64EncodingType);
            writer.WriteString(Convert.ToBase64String(Nonce));
            writer.WriteEndElement();

            writer.WriteElementString("wsu", "Created", WsuNamespace, Created);

            writer.WriteEndElement();
        }
    }
}
