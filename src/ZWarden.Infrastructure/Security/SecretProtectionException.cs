namespace ZWarden.Infrastructure.Security;

/// <summary>Why a secret could not be unprotected. Carried on <see cref="SecretProtectionException"/> for diagnostics.</summary>
public enum SecretProtectionFailure
{
    /// <summary>The envelope is not well-formed (bad base64, unknown version, or truncated).</summary>
    Malformed,

    /// <summary>The envelope names a key id that is not in the key ring.</summary>
    UnknownKey,

    /// <summary>Authentication failed - the envelope was tampered with, or decrypted under the wrong key.</summary>
    Tampered,
}

/// <summary>
/// Thrown when <see cref="SecretProtector"/> cannot decrypt an envelope. Carries a
/// <see cref="SecretProtectionFailure"/> category so operators get an actionable signal; the message
/// never contains key material or plaintext (trust-boundaries.md §2).
/// </summary>
public sealed class SecretProtectionException : Exception
{
    public SecretProtectionException()
    {
    }

    public SecretProtectionException(string message)
        : base(message)
    {
    }

    public SecretProtectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SecretProtectionException(SecretProtectionFailure failure, string message)
        : base(message)
    {
        Failure = failure;
    }

    public SecretProtectionException(SecretProtectionFailure failure, string message, Exception innerException)
        : base(message, innerException)
    {
        Failure = failure;
    }

    /// <summary>The failure category. Defaults to <see cref="SecretProtectionFailure.Malformed"/>.</summary>
    public SecretProtectionFailure Failure { get; }
}
