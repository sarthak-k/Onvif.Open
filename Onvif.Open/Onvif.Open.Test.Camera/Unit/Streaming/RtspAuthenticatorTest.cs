// Sarthak Killedar
// 2026-11-07

using System.Net;
using Onvif.Open.Streaming.Implementation.Authentication;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Streaming
{
    public class RtspAuthenticatorTest
    {
        #region Public Methods

        [Fact]
        public void DigestShouldMatchRfc2617Example()
        {
            var challenge = RtspAuthenticatorFactory.ParseParameters(
                " realm=\"testrealm@host.com\", qop=\"auth,auth-int\", nonce=\"dcd98b7102dd2f0e8b11d0f600bfb0c093\", opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"");

            var authenticator = new DigestRtspAuthenticator(new NetworkCredential("Mufasa", "Circle Of Life"), challenge, "0a4f113b");

            var header = authenticator.GetAuthorization("GET", "/dir/index.html");

            Assert.Contains("response=\"6629fae49393a05397450978507c4ef1\"", header);
            Assert.Contains("nc=00000001", header);
            Assert.Contains("opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"", header);
        }

        [Fact]
        public void FactoryShouldPreferDigestOverBasic()
        {
            var authenticator = RtspAuthenticatorFactory.Create(
                new NetworkCredential("admin", "12345"),
                new[] { "Basic realm=\"cam\"", "Digest realm=\"cam\", nonce=\"abc\"" });

            Assert.IsType<DigestRtspAuthenticator>(authenticator);
        }

        [Fact]
        public void FactoryShouldUseBasicWhenOnlyBasicIsOffered()
        {
            var authenticator = RtspAuthenticatorFactory.Create(new NetworkCredential("admin", "12345"), new[] { "Basic realm=\"cam\"" });

            Assert.Equal("Basic YWRtaW46MTIzNDU=", authenticator.GetAuthorization("DESCRIBE", "rtsp://cam/stream"));
        }

        [Fact]
        public void FactoryShouldReturnNullWithoutCredentials()
        {
            Assert.Null(RtspAuthenticatorFactory.Create(null, new[] { "Basic realm=\"cam\"" }));
        }

        #endregion
    }
}
