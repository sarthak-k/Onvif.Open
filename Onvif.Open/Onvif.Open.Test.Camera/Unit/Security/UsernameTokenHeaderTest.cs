// Sarthak Killedar
// 2026-11-07

using System;
using System.Globalization;
using System.Linq;
using Onvif.Open.Core.Implementation.Security;
using Xunit;

namespace Onvif.Open.Test.Camera.Unit.Security
{
    public class UsernameTokenHeaderTest
    {
        #region Public Methods

        [Fact]
        public void PasswordDigestShouldMatchWsSecuritySpecification()
        {
            var nonce = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
            var created = DateTime.Parse("2026-11-07T10:00:00.000Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

            var header = new UsernameTokenHeader("admin", "12345", nonce, created);

            Assert.Equal("2026-11-07T10:00:00.000Z", header.Created);
            Assert.Equal("zmA8ktJ4GnL4sScITiMm1og7t1Q=", header.PasswordDigest);
        }

        [Fact]
        public void EachTokenShouldUseAFreshNonce()
        {
            var first = new UsernameTokenHeader("admin", "12345");
            var second = new UsernameTokenHeader("admin", "12345");

            Assert.NotEqual(Convert.ToBase64String(first.Nonce), Convert.ToBase64String(second.Nonce));
        }

        #endregion
    }
}
