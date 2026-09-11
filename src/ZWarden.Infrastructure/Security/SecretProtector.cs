using System.Security.Cryptography;
using System.Text;
using ZWarden.Domain.Security;

namespace ZWarden.Infrastructure.Security;

/// <summary>
/// Application-layer authenticated encryption over <see cref="AesGcm"/> (ADR 0015). Every value is
/// encrypted under a <b>fresh per-message subkey</b> derived as
/// <c>HKDF-SHA256(masterKey, salt = 32 random bytes, info)</c>, so each AES-GCM key encrypts exactly
/// once - making both nonce reuse and NIST SP 800-38D's 2^32-invocation cap unreachable by
/// construction rather than defended by a counter. The self-describing envelope is
/// <c>version | keyIdLen | keyId | salt | nonce | ciphertext | tag</c>, base64-encoded, with an
/// explicit 16-byte tag (never the obsolete tag-size-less <see cref="AesGcm"/> constructor).
/// </summary>
public sealed class SecretProtector : ISecretProtector
{
    private const byte EnvelopeVersion = 1;
    private const int SaltSize = 32;
    private const int NonceSize = 12; // AES-GCM 96-bit nonce (AesGcm.NonceByteSizes max).
    private const int TagSize = 16;   // AES-GCM 128-bit tag (explicit; SYSLIB0053).
    private const int SubkeySize = 32; // AES-256.
    private static readonly byte[] Info = Encoding.UTF8.GetBytes("ZWarden.SecretProtector.v1");

    private readonly IKeyRing _keyRing;

    public SecretProtector(IKeyRing keyRing)
    {
        ArgumentNullException.ThrowIfNull(keyRing);
        _keyRing = keyRing;
    }

    public string Protect(ReadOnlySpan<byte> plaintext)
    {
        string keyId = _keyRing.ActiveKeyId;
        byte[] keyIdBytes = Encoding.UTF8.GetBytes(keyId);

        Span<byte> salt = stackalloc byte[SaltSize];
        Span<byte> nonce = stackalloc byte[NonceSize];
        RandomNumberGenerator.Fill(salt);
        RandomNumberGenerator.Fill(nonce);

        byte[] ciphertext = new byte[plaintext.Length];
        Span<byte> tag = stackalloc byte[TagSize];
        Span<byte> subkey = stackalloc byte[SubkeySize];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, _keyRing.GetKey(keyId).Span, subkey, salt, Info);
            using AesGcm gcm = new(subkey, TagSize);
            gcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
        }

        byte[] envelope = new byte[2 + keyIdBytes.Length + SaltSize + NonceSize + ciphertext.Length + TagSize];
        int offset = 0;
        envelope[offset++] = EnvelopeVersion;
        envelope[offset++] = checked((byte)keyIdBytes.Length);
        offset += Write(envelope, offset, keyIdBytes);
        offset += Write(envelope, offset, salt);
        offset += Write(envelope, offset, nonce);
        offset += Write(envelope, offset, ciphertext);
        Write(envelope, offset, tag);

        return Convert.ToBase64String(envelope);
    }

    public byte[] Unprotect(string envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(envelope);
        }
        catch (FormatException ex)
        {
            throw new SecretProtectionException(
                SecretProtectionFailure.Malformed, "The secret envelope is not valid base64.", ex);
        }

        if (bytes.Length < 2 || bytes[0] != EnvelopeVersion)
        {
            throw new SecretProtectionException(
                SecretProtectionFailure.Malformed, "The secret envelope has an unsupported version or is truncated.");
        }

        int keyIdLength = bytes[1];
        int headerAndFixed = 2 + keyIdLength + SaltSize + NonceSize + TagSize;
        if (keyIdLength == 0 || bytes.Length < headerAndFixed)
        {
            throw new SecretProtectionException(
                SecretProtectionFailure.Malformed, "The secret envelope is truncated or malformed.");
        }

        int offset = 2;
        string keyId = Encoding.UTF8.GetString(bytes, offset, keyIdLength);
        offset += keyIdLength;

        if (!_keyRing.Contains(keyId))
        {
            throw new SecretProtectionException(
                SecretProtectionFailure.UnknownKey,
                $"The secret envelope names key id '{keyId}', which is not in the key ring.");
        }

        ReadOnlySpan<byte> salt = bytes.AsSpan(offset, SaltSize);
        offset += SaltSize;
        ReadOnlySpan<byte> nonce = bytes.AsSpan(offset, NonceSize);
        offset += NonceSize;
        int ciphertextLength = bytes.Length - offset - TagSize;
        ReadOnlySpan<byte> ciphertext = bytes.AsSpan(offset, ciphertextLength);
        offset += ciphertextLength;
        ReadOnlySpan<byte> tag = bytes.AsSpan(offset, TagSize);

        byte[] plaintext = new byte[ciphertextLength];
        Span<byte> subkey = stackalloc byte[SubkeySize];
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, _keyRing.GetKey(keyId).Span, subkey, salt, Info);
            using AesGcm gcm = new(subkey, TagSize);
            gcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new SecretProtectionException(
                SecretProtectionFailure.Tampered,
                "The secret envelope failed authentication - it was tampered with, or encrypted under a different key.",
                ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(subkey);
        }

        return plaintext;
    }

    public string ProtectString(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return Protect(Encoding.UTF8.GetBytes(plaintext));
    }

    public string UnprotectString(string envelope)
    {
        byte[] plaintext = Unprotect(envelope);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static int Write(byte[] destination, int offset, ReadOnlySpan<byte> source)
    {
        source.CopyTo(destination.AsSpan(offset));
        return source.Length;
    }
}
