// Sarthak Killedar
// 2026-11-11

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace Onvif.Open.Recorder.Security
{
    /// <summary>A SAML response that is not to be trusted, and why.</summary>
    public sealed class SamlException : Exception
    {
        public SamlException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Who a checked SAML response says signed in.</summary>
    public sealed record SamlIdentity(string Issuer, string NameId, string Email);

    /// <summary>
    /// SAML 2.0 for single sign-on, the service provider's side: a signed-in request sent with the HTTP-Redirect
    /// binding, and the response the identity provider posts back (HTTP-POST binding) checked strictly: one assertion,
    /// signed (or in a signed response) by the configured certificate with SHA-256 or stronger, the signed element being
    /// the one that is read (no signature wrapping), issued by the provider for this recorder, to this address, in answer
    /// to this request, and current. Encrypted assertions and sign-ins started at the provider are not supported.
    /// </summary>
    public static class Saml
    {
        public const string Protocol = "urn:oasis:names:tc:SAML:2.0:protocol";
        public const string Assertion = "urn:oasis:names:tc:SAML:2.0:assertion";
        public const string Success = "urn:oasis:names:tc:SAML:2.0:status:Success";
        public const string Bearer = "urn:oasis:names:tc:SAML:2.0:cm:bearer";

        public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

        private static readonly string[] SignatureMethods =
        {
            "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256",
            "http://www.w3.org/2001/04/xmldsig-more#rsa-sha384",
            "http://www.w3.org/2001/04/xmldsig-more#rsa-sha512",
            "http://www.w3.org/2007/05/xmldsig-more#sha256-rsa-MGF1",
            "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256",
            "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha384",
            "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha512"
        };

        private static readonly string[] DigestMethods =
        {
            "http://www.w3.org/2001/04/xmlenc#sha256",
            "http://www.w3.org/2001/04/xmldsig-more#sha384",
            "http://www.w3.org/2001/04/xmlenc#sha512"
        };

        private static readonly string[] Transforms =
        {
            "http://www.w3.org/2000/09/xmldsig#enveloped-signature",
            "http://www.w3.org/2001/10/xml-exc-c14n#",
            "http://www.w3.org/2001/10/xml-exc-c14n#WithComments"
        };

        /// <summary>A request's id: the response must name it (InResponseTo).</summary>
        public static string NewRequestId() => "_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        /// <summary>The provider's sign-in page with the request <paramref name="id"/> (HTTP-Redirect binding, unsigned).</summary>
        public static string CreateRequest(string ssoUrl, string id, string serviceProviderId, string assertionConsumerUrl, string relayState, DateTimeOffset now, string loginHint = null)
        {
            var request = new StringBuilder()
                .Append($"<samlp:AuthnRequest xmlns:samlp=\"{Protocol}\" xmlns:saml=\"{Assertion}\" ID=\"{id}\" Version=\"2.0\" IssueInstant=\"{now.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\"")
                .Append($" Destination=\"{Escape(ssoUrl)}\" AssertionConsumerServiceURL=\"{Escape(assertionConsumerUrl)}\" ProtocolBinding=\"urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST\">")
                .Append($"<saml:Issuer>{Escape(serviceProviderId)}</saml:Issuer>")
                .Append("<samlp:NameIDPolicy Format=\"urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress\" AllowCreate=\"true\"/>")
                .Append(loginHint == null ? string.Empty : $"<saml:Subject><saml:NameID Format=\"urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress\">{Escape(loginHint)}</saml:NameID></saml:Subject>")
                .Append("</samlp:AuthnRequest>")
                .ToString();

            using var buffer = new MemoryStream();
            using (var deflate = new DeflateStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(Encoding.UTF8.GetBytes(request));
            }

            var separator = ssoUrl.Contains('?') ? "&" : "?";
            return $"{ssoUrl}{separator}SAMLRequest={Uri.EscapeDataString(Convert.ToBase64String(buffer.ToArray()))}&RelayState={Uri.EscapeDataString(relayState)}";
        }

        /// <summary>The certificate a provider signs with, from its PEM or bare base64 (as metadata carries it).</summary>
        public static X509Certificate2 ReadCertificate(string text)
        {
            var body = (text ?? string.Empty).Replace("-----BEGIN CERTIFICATE-----", string.Empty).Replace("-----END CERTIFICATE-----", string.Empty);
            body = new string(body.Where(c => !char.IsWhiteSpace(c)).ToArray());
            return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(body));
        }

        /// <summary>The identity in a posted response, after every check; throws <see cref="SamlException"/> otherwise.</summary>
        public static SamlIdentity Validate(string samlResponse, X509Certificate2 certificate, string providerId, string serviceProviderId, string assertionConsumerUrl, string requestId, DateTimeOffset now)
        {
            XmlDocument document;
            try
            {
                document = Load(Encoding.UTF8.GetString(Convert.FromBase64String(samlResponse ?? string.Empty)));
            }
            catch (Exception ex) when (ex is FormatException || ex is XmlException)
            {
                throw new SamlException("The response is not SAML.");
            }

            var ns = new XmlNamespaceManager(document.NameTable);
            ns.AddNamespace("samlp", Protocol);
            ns.AddNamespace("saml", Assertion);
            ns.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

            var response = document.DocumentElement;
            if (response?.LocalName != "Response" || response.NamespaceURI != Protocol)
            {
                throw new SamlException("The response is not a SAML response.");
            }

            if (response.SelectNodes("//saml:EncryptedAssertion", ns)!.Count > 0)
            {
                throw new SamlException("Encrypted assertions are not supported: turn assertion encryption off at the provider.");
            }

            var assertions = response.SelectNodes("//saml:Assertion", ns)!;
            if (assertions.Count != 1 || assertions[0]!.ParentNode != response)
            {
                throw new SamlException("The response must hold exactly one assertion.");
            }

            var assertion = (XmlElement)assertions[0];

            // Signed: the assertion itself, or the whole response (which holds it). Either way that element is the one read.
            var assertionSigned = IsSignedBy(assertion, certificate, ns);
            if (!assertionSigned && !IsSignedBy(response, certificate, ns))
            {
                throw new SamlException("The response is not signed by the provider's certificate.");
            }

            if (response.SelectSingleNode("samlp:Status/samlp:StatusCode/@Value", ns)?.Value != Success)
            {
                throw new SamlException("The provider did not sign you in.");
            }

            if (response.GetAttribute("InResponseTo") != requestId)
            {
                throw new SamlException("The response does not answer this sign-in.");
            }

            var destination = response.GetAttribute("Destination");
            if (!string.IsNullOrEmpty(destination) && destination != assertionConsumerUrl)
            {
                throw new SamlException("The response is for another address.");
            }

            var issuer = assertion.SelectSingleNode("saml:Issuer", ns)?.InnerText.Trim();
            if (issuer != providerId)
            {
                throw new SamlException("The response is from another provider.");
            }

            var conditions = assertion.SelectSingleNode("saml:Conditions", ns) as XmlElement;
            if (conditions == null || !InTime(conditions, now) || conditions.SelectNodes("saml:AudienceRestriction", ns)!.Cast<XmlElement>()
                    .Any(restriction => restriction.SelectNodes("saml:Audience", ns)!.Cast<XmlNode>().All(a => a.InnerText.Trim() != serviceProviderId))
                || conditions.SelectNodes("saml:AudienceRestriction", ns)!.Count == 0)
            {
                throw new SamlException("The assertion is not for this recorder, or not now.");
            }

            var confirmed = assertion.SelectNodes("saml:Subject/saml:SubjectConfirmation[@Method='" + Bearer + "']/saml:SubjectConfirmationData", ns)!.Cast<XmlElement>()
                .Any(data => data.GetAttribute("Recipient") == assertionConsumerUrl
                    && (string.IsNullOrEmpty(data.GetAttribute("InResponseTo")) || data.GetAttribute("InResponseTo") == requestId)
                    && DateTimeOffset.TryParse(data.GetAttribute("NotOnOrAfter"), out var until) && now < until + ClockSkew);
            if (!confirmed)
            {
                throw new SamlException("The assertion is not for this sign-in.");
            }

            var nameId = assertion.SelectSingleNode("saml:Subject/saml:NameID", ns)?.InnerText.Trim();
            if (string.IsNullOrEmpty(nameId))
            {
                throw new SamlException("The provider did not say who you are.");
            }

            var email = Attribute(assertion, ns, "email", "mail", "emailaddress", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress", "urn:oid:0.9.2342.19200300.100.1.3")
                ?? (nameId.Contains('@') ? nameId : null);
            return new SamlIdentity(issuer, nameId, email);
        }

        private static XmlDocument Load(string xml)
        {
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
            return document;
        }

        /// <summary>
        /// Whether <paramref name="element"/> carries its own signature, by <paramref name="certificate"/>, over itself
        /// (its reference is its ID, the only element with that ID), with strong algorithms and the expected transforms.
        /// </summary>
        private static bool IsSignedBy(XmlElement element, X509Certificate2 certificate, XmlNamespaceManager ns)
        {
            var signatures = element.SelectNodes("ds:Signature", ns)!;
            var id = element.GetAttribute("ID");
            if (signatures.Count != 1 || string.IsNullOrEmpty(id) || element.OwnerDocument.SelectNodes($"//*[@ID='{id.Replace("'", string.Empty)}']")!.Count != 1)
            {
                return false;
            }

            var signed = new SignedXml(element);
            signed.LoadXml((XmlElement)signatures[0]);
            if (!SignatureMethods.Contains(signed.SignedInfo.SignatureMethod) || signed.SignedInfo.References.Count != 1)
            {
                return false;
            }

            var reference = (Reference)signed.SignedInfo.References[0];
            if (reference.Uri != "#" + id || !DigestMethods.Contains(reference.DigestMethod))
            {
                return false;
            }

            for (var i = 0; i < reference.TransformChain.Count; i++)
            {
                if (!Transforms.Contains(reference.TransformChain[i].Algorithm))
                {
                    return false;
                }
            }

            return signed.CheckSignature(certificate, verifySignatureOnly: true);
        }

        private static bool InTime(XmlElement conditions, DateTimeOffset now)
        {
            var notBefore = conditions.GetAttribute("NotBefore");
            var notOnOrAfter = conditions.GetAttribute("NotOnOrAfter");
            return (string.IsNullOrEmpty(notBefore) || (DateTimeOffset.TryParse(notBefore, out var from) && now >= from - ClockSkew))
                && (string.IsNullOrEmpty(notOnOrAfter) || (DateTimeOffset.TryParse(notOnOrAfter, out var until) && now < until + ClockSkew));
        }

        private static string Attribute(XmlElement assertion, XmlNamespaceManager ns, params string[] names)
        {
            foreach (XmlElement attribute in assertion.SelectNodes("saml:AttributeStatement/saml:Attribute", ns)!)
            {
                if (names.Any(n => string.Equals(n, attribute.GetAttribute("Name"), StringComparison.OrdinalIgnoreCase)))
                {
                    var value = attribute.SelectSingleNode("saml:AttributeValue", ns)?.InnerText.Trim();
                    if (!string.IsNullOrEmpty(value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        private static string Escape(string text) => System.Security.SecurityElement.Escape(text);
    }
}
