using System.Text.RegularExpressions;

namespace ZWarden.Infrastructure.Security;

/// <summary>
/// Builds a <see cref="KeyRing"/> from configuration and <b>fails closed</b> (ADR 0015): a missing,
/// short, unparseable, or active-less configuration throws <see cref="KeyRingConfigurationException"/>
/// rather than yielding a weak or empty ring. The wire format is a set of <c>keyId:base64key</c>
/// entries separated by <c>;</c>, plus the id of the active key. Key ids are not secret; key bytes
/// never appear in an exception message.
/// </summary>
public static partial class KeyRingLoader
{
    private const string KeysEnvVar = "ZW_SECRET_KEYS";
    private const string KeysFileEnvVar = "ZW_SECRET_KEYS_FILE";
    private const string ActiveKeyEnvVar = "ZW_SECRET_ACTIVE_KEY_ID";

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex KeyIdPattern();

    /// <summary>
    /// Parses <paramref name="keysValue"/> (<c>keyId:base64;keyId:base64</c>) and selects
    /// <paramref name="activeKeyId"/> as the encryption key. Throws <see cref="KeyRingConfigurationException"/>
    /// on any problem.
    /// </summary>
    public static KeyRing Load(string? keysValue, string? activeKeyId)
    {
        if (string.IsNullOrWhiteSpace(keysValue))
        {
            throw new KeyRingConfigurationException(
                $"No secret keys are configured ({KeysEnvVar} is empty). ZWarden fails closed without a key.");
        }

        if (string.IsNullOrWhiteSpace(activeKeyId))
        {
            throw new KeyRingConfigurationException(
                $"No active secret key id is configured ({ActiveKeyEnvVar} is empty).");
        }

        Dictionary<string, byte[]> keys = new(StringComparer.Ordinal);
        foreach (string rawEntry in keysValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = rawEntry.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == rawEntry.Length - 1)
            {
                throw new KeyRingConfigurationException(
                    "A secret key entry is malformed; expected 'keyId:base64key'.");
            }

            string keyId = rawEntry[..separator].Trim();
            string base64 = rawEntry[(separator + 1)..].Trim();

            if (!KeyIdPattern().IsMatch(keyId))
            {
                throw new KeyRingConfigurationException(
                    $"Secret key id '{keyId}' is invalid; use 1-64 chars of [A-Za-z0-9_-].");
            }

            if (keys.ContainsKey(keyId))
            {
                throw new KeyRingConfigurationException($"Duplicate secret key id '{keyId}'.");
            }

            byte[] key;
            try
            {
                key = Convert.FromBase64String(base64);
            }
            catch (FormatException ex)
            {
                throw new KeyRingConfigurationException($"Secret key '{keyId}' is not valid base64.", ex);
            }

            if (key.Length != KeyRing.KeySizeBytes)
            {
                throw new KeyRingConfigurationException(
                    $"Secret key '{keyId}' must decode to {KeyRing.KeySizeBytes} bytes (256-bit); got {key.Length}.");
            }

            keys[keyId] = key;
        }

        // KeyRing re-validates size and active-key membership; the loader owns parsing and format.
        return new KeyRing(keys, activeKeyId);
    }

    /// <summary>
    /// Loads from the environment: keys from <c>ZW_SECRET_KEYS</c> or, if a path is given in
    /// <c>ZW_SECRET_KEYS_FILE</c>, the contents of that file; active id from
    /// <c>ZW_SECRET_ACTIVE_KEY_ID</c>. Fails closed like <see cref="Load"/>.
    /// </summary>
    public static KeyRing LoadFromEnvironment()
    {
        string? keysValue = Environment.GetEnvironmentVariable(KeysEnvVar);
        string? keysFile = Environment.GetEnvironmentVariable(KeysFileEnvVar);
        if (string.IsNullOrWhiteSpace(keysValue) && !string.IsNullOrWhiteSpace(keysFile))
        {
            try
            {
                keysValue = File.ReadAllText(keysFile);
            }
            catch (IOException ex)
            {
                throw new KeyRingConfigurationException(
                    $"Could not read the secret-keys file named by {KeysFileEnvVar}.", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new KeyRingConfigurationException(
                    $"Access denied reading the secret-keys file named by {KeysFileEnvVar}.", ex);
            }
        }

        return Load(keysValue, Environment.GetEnvironmentVariable(ActiveKeyEnvVar));
    }
}
