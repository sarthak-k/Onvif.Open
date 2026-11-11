// Sarthak Killedar
// 2026-11-11

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Onvif.Open.Recorder.Security
{
    /// <summary>
    /// Time-based one-time passwords (RFC 6238, as authenticator apps make them): HMAC-SHA1, 6 digits, 30-second steps.
    /// </summary>
    public static class Totp
    {
        public const int Digits = 6;
        public static readonly TimeSpan Step = TimeSpan.FromSeconds(30);

        /// <summary>Steps either side of now that are accepted (clocks of phones drift).</summary>
        public const int Window = 1;

        /// <summary>A new secret: 160 random bits, as RFC 4226 recommends.</summary>
        public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

        public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / (long)Step.TotalSeconds;

        /// <summary>The code for one step.</summary>
        public static string Code(byte[] secret, long step)
        {
            Span<byte> counter = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
            Span<byte> hash = stackalloc byte[20];
            HMACSHA1.HashData(secret, counter, hash);

            var offset = hash[19] & 0x0F;
            var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
            return (value % 1_000_000).ToString("D6");
        }

        /// <summary>
        /// The step <paramref name="code"/> belongs to, near <paramref name="now"/> and after <paramref name="lastUsed"/>
        /// (a code works once); null when it is wrong.
        /// </summary>
        public static long? Verify(byte[] secret, string code, DateTimeOffset now, long lastUsed)
        {
            var digits = Normalize(code);
            if (digits == null)
            {
                return null;
            }

            var current = StepAt(now);
            for (var step = current - Window; step <= current + Window; step++)
            {
                if (step > lastUsed && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(digits)))
                {
                    return step;
                }
            }

            return null;
        }

        /// <summary>The 6 digits of a code as typed ("123 456"), or null.</summary>
        public static string Normalize(string code)
        {
            var digits = (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
            return digits.Length == Digits && digits.All(char.IsAsciiDigit) ? digits : null;
        }

        /// <summary>What authenticator apps scan (a QR code of it) or take by link.</summary>
        public static string Uri(string issuer, string account, byte[] secret) =>
            $"otpauth://totp/{System.Uri.EscapeDataString(issuer)}:{System.Uri.EscapeDataString(account)}?secret={Base32(secret)}&issuer={System.Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={(int)Step.TotalSeconds}";

        /// <summary>RFC 4648 base32 without padding: how secrets are typed into apps.</summary>
        public static string Base32(byte[] data)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
            var text = new StringBuilder((data.Length * 8 + 4) / 5);
            int buffer = 0, bits = 0;
            foreach (var b in data)
            {
                buffer = (buffer << 8) | b;
                bits += 8;
                while (bits >= 5)
                {
                    text.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                    bits -= 5;
                }
            }

            if (bits > 0)
            {
                text.Append(alphabet[(buffer << (5 - bits)) & 31]);
            }

            return text.ToString();
        }
    }
}
