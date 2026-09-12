using System.Text;
using System.Text.Json;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Trust;

/// <summary>
/// The file-backed <see cref="IAgentTrustStore"/> (F9). Trust material is stored as a small JSON object in a
/// single UTF-8, BOM-less file; writes go via a temp file plus an atomic replace, so a torn write can never
/// leave half-written trust. A single file, never a database (trust-boundaries.md §9 rule 1).
/// <para>
/// The credential sits in this file as plaintext: that is the accepted residual risk of the bearer-credential
/// model (ADR 0007 — a host compromise is a credential compromise, and the concrete host secret store is
/// still open, PRD 10). It is <b>revocable and rotatable</b>, which is what bounds the exposure.
/// </para>
/// </summary>
public sealed class FileAgentTrustStore : IAgentTrustStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _filePath;

    /// <summary>Creates the store over the trust file at <paramref name="filePath"/>.</summary>
    public FileAgentTrustStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    /// <inheritdoc />
    public async Task<AgentTrustMaterial?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        string contents = await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false);

        StoredTrust? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredTrust>(contents);
        }
        catch (JsonException ex)
        {
            throw new AgentTrustException(
                $"The Agent trust file '{_filePath}' is not valid JSON. Refusing to overwrite it; resolve or remove the file.",
                ex);
        }

        if (stored is null
            || string.IsNullOrWhiteSpace(stored.AgentId)
            || string.IsNullOrWhiteSpace(stored.Credential)
            || !AgentId.TryParse(stored.AgentId, out AgentId agentId))
        {
            throw new AgentTrustException(
                $"The Agent trust file '{_filePath}' does not contain valid trust material. " +
                "Refusing to overwrite it; resolve or remove the file.");
        }

        return new AgentTrustMaterial(agentId, new SecretString(stored.Credential), stored.Label);
    }

    /// <inheritdoc />
    public async Task SaveAsync(AgentTrustMaterial material, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(material);

        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            new StoredTrust(material.AgentId.ToString(), material.Credential.Reveal(), material.Label));

        string tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, Utf8NoBom, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    // The on-disk shape: plain strings only (SecretString cannot be JSON-serialized by design).
    private sealed record StoredTrust(string AgentId, string Credential, string? Label);
}
