using System.Collections.Frozen;
using ZWarden.Domain.Security;

namespace ZWarden.Infrastructure.Security;

/// <summary>
/// An in-memory <see cref="IKeyRing"/> (ADR 0015): one active key for encryption, every key available
/// for decryption. Keys are held in process memory only, never persisted to the application database
/// (PRD 10). Construct it through <see cref="KeyRingLoader"/>, which validates and fails closed.
/// </summary>
public sealed class KeyRing : IKeyRing
{
    /// <summary>The required key length: 256 bits for AES-256 (ADR 0015).</summary>
    public const int KeySizeBytes = 32;

    private readonly FrozenDictionary<string, byte[]> _keys;

    /// <summary>
    /// Builds a ring from validated material. Prefer <see cref="KeyRingLoader"/> over calling this
    /// directly; it enforces the same invariants against untrusted configuration.
    /// </summary>
    public KeyRing(IReadOnlyDictionary<string, byte[]> keys, string activeKeyId)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentException.ThrowIfNullOrWhiteSpace(activeKeyId);

        if (keys.Count == 0)
        {
            throw new KeyRingConfigurationException("The key ring is empty; at least one key is required.");
        }

        foreach (KeyValuePair<string, byte[]> entry in keys)
        {
            if (entry.Value is null || entry.Value.Length != KeySizeBytes)
            {
                throw new KeyRingConfigurationException(
                    $"Key '{entry.Key}' must be {KeySizeBytes} bytes (256-bit).");
            }
        }

        if (!keys.ContainsKey(activeKeyId))
        {
            throw new KeyRingConfigurationException(
                $"Active key id '{activeKeyId}' is not among the configured keys.");
        }

        _keys = keys.ToFrozenDictionary(StringComparer.Ordinal);
        ActiveKeyId = activeKeyId;
    }

    public string ActiveKeyId { get; }

    public bool Contains(string keyId) => _keys.ContainsKey(keyId);

    public ReadOnlyMemory<byte> GetKey(string keyId)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        return _keys.TryGetValue(keyId, out byte[]? key)
            ? key
            : throw new KeyNotFoundException($"No key with id '{keyId}' is present in the key ring.");
    }
}
