using System.Text;
using Microsoft.Extensions.Logging;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Identity;

/// <summary>
/// The file-backed <see cref="IAgentIdentityStore"/> (F8). The identity is stored as its canonical
/// <c>agt-&lt;uuid&gt;</c> string in a single UTF-8, BOM-less file; writes go via a temp file plus an
/// atomic replace so a torn write can never leave a half-written identity. A single file, never a
/// database — the Agent must not reach persistence (trust-boundaries.md §9 rule 1).
/// </summary>
public sealed partial class FileAgentIdentityStore : IAgentIdentityStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _filePath;
    private readonly ILogger<FileAgentIdentityStore> _logger;

    /// <summary>Creates the store over the identity file at <paramref name="filePath"/>.</summary>
    public FileAgentIdentityStore(string filePath, ILogger<FileAgentIdentityStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(logger);
        _filePath = filePath;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AgentId?> TryLoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        string contents = (await File.ReadAllTextAsync(_filePath, cancellationToken).ConfigureAwait(false)).Trim();
        if (!AgentId.TryParse(contents, out AgentId agentId))
        {
            throw new AgentIdentityException(
                $"The Agent identity file '{_filePath}' does not contain a valid Agent id. " +
                "Refusing to overwrite it with a new identity; resolve or remove the file.");
        }

        return agentId;
    }

    /// <inheritdoc />
    public async Task SaveAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, agentId.ToString(), Utf8NoBom, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc />
    public async Task<AgentId> LoadOrCreateAsync(CancellationToken cancellationToken = default)
    {
        AgentId? existing = await TryLoadAsync(cancellationToken).ConfigureAwait(false);
        if (existing is { } agentId)
        {
            LogLoaded(agentId, _filePath);
            return agentId;
        }

        AgentId created = AgentId.New();
        await SaveAsync(created, cancellationToken).ConfigureAwait(false);
        LogCreated(created, _filePath);
        return created;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded Agent identity {AgentId} from {IdentityFilePath}.")]
    private partial void LogLoaded(AgentId agentId, string identityFilePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated new Agent identity {AgentId} and persisted it to {IdentityFilePath}.")]
    private partial void LogCreated(AgentId agentId, string identityFilePath);
}
