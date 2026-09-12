using System.Globalization;
using System.Security.Cryptography;

namespace ZWarden.TestSupport;

/// <summary>
/// A minimal RFC 6238 TOTP generator for tests, matching ASP.NET Core Identity's authenticator
/// (HMAC-SHA1, 30-second step, 6 digits, no modifier) so a test can produce a code that Identity's
/// verifier accepts.
/// </summary>
public static class Totp
{
    /// <summary>Computes the current 6-digit code for an unformatted base32 authenticator key.</summary>
    public static string Compute(string base32Key)
    {
        ArgumentNullException.ThrowIfNull(base32Key);
        byte[] key = Base32Decode(base32Key);
        long timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

        byte[] counter = BitConverter.GetBytes(timestep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        // TOTP authenticators mandate HMAC-SHA1 - interop with Identity, not a security choice.
#pragma warning disable CA5350
        byte[] hash = HMACSHA1.HashData(key, counter);
#pragma warning restore CA5350
        int offset = hash[^1] & 0x0f;
        int binary = ((hash[offset] & 0x7f) << 24)
            | ((hash[offset + 1] & 0xff) << 16)
            | ((hash[offset + 2] & 0xff) << 8)
            | (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=').ToUpperInvariant();
        List<byte> output = [];
        int bits = 0, value = 0;
        foreach (char c in input)
        {
            value = (value << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }

        return [.. output];
    }
}
