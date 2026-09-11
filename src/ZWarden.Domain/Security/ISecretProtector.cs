namespace ZWarden.Domain.Security;

/// <summary>
/// Application-layer authenticated encryption for secrets (PRD 10, ADR 0015). A protected value is a
/// self-describing envelope string - <c>version | keyId | salt | nonce | ciphertext | tag</c>, base64 -
/// that round-trips unchanged on both database providers. Decryption of a tampered or wrong-key
/// envelope throws; it never returns wrong plaintext.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Encrypts <paramref name="plaintext"/> under the active key, returning the envelope string.</summary>
    string Protect(ReadOnlySpan<byte> plaintext);

    /// <summary>Decrypts and authenticates an <paramref name="envelope"/> produced by <see cref="Protect"/>.</summary>
    byte[] Unprotect(string envelope);

    /// <summary>UTF-8 convenience over <see cref="Protect(ReadOnlySpan{byte})"/>.</summary>
    string ProtectString(string plaintext);

    /// <summary>UTF-8 convenience over <see cref="Unprotect(string)"/>.</summary>
    string UnprotectString(string envelope);
}
