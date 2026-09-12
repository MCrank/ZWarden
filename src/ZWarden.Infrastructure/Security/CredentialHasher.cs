using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using ZWarden.Application.Agents;
using ZWarden.Domain.Security;

namespace ZWarden.Infrastructure.Security;

/// <summary>
/// The <see cref="ICredentialHasher"/> for F9's bearer secrets (ADR 0007, decision 2). Generation is
/// 32 bytes (256-bit) from <see cref="RandomNumberGenerator"/>, base64url-encoded and prefixed; hashing
/// is a single <see cref="SHA256"/> over the UTF-8 secret. No salt or KDF is used and none is needed —
/// the secret is full-entropy, so there is nothing to brute-force, and this keeps clear of the AEAD/KDF
/// confinement the security foundation (ADR 0015) reserves. A leaked hash is not a usable credential.
/// </summary>
public sealed class CredentialHasher : ICredentialHasher
{
    private const int SecretByteCount = 32; // 256-bit.

    /// <inheritdoc />
    public SecretString Generate(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        byte[] random = RandomNumberGenerator.GetBytes(SecretByteCount);
        string token = Base64Url.EncodeToString(random);
        return new SecretString($"{prefix}_{token}");
    }

    /// <inheritdoc />
    public string Hash(SecretString secret)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(secret.Reveal()));
        return Convert.ToHexStringLower(digest);
    }
}
