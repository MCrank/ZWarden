namespace ZWarden.Domain.Security;

/// <summary>
/// The set of master keys available to <see cref="ISecretProtector"/> (ADR 0015). One key is active
/// for encryption; every key remains available for decryption so that values written under a retired
/// key still decrypt. Keys are never persisted to the application database (PRD 10). A loader that
/// cannot supply a valid, active key fails closed rather than returning an empty ring.
/// </summary>
public interface IKeyRing
{
    /// <summary>The id of the key new values are encrypted under.</summary>
    string ActiveKeyId { get; }

    /// <summary>True when <paramref name="keyId"/> is present in the ring.</summary>
    bool Contains(string keyId);

    /// <summary>Returns the 32-byte key for <paramref name="keyId"/>, or throws if it is not in the ring.</summary>
    ReadOnlyMemory<byte> GetKey(string keyId);
}
