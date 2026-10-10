using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Backups;

/// <summary>The result of a backup the Agent produced host-side (F24).</summary>
/// <param name="Succeeded">Whether the archive was written and checksummed.</param>
/// <param name="ArchiveName">On success, the archive's relative locator (its file name under
/// <c>&lt;BackupRoot&gt;/&lt;ServerId&gt;/</c>); <c>null</c> otherwise.</param>
/// <param name="SizeBytes">On success, the produced archive's byte size; <c>0</c> otherwise.</param>
/// <param name="Sha256">On success, the lowercase-hex SHA-256 over the archive; <c>null</c> otherwise.</param>
/// <param name="CreatedAt">On success, when the archive was written (UTC); <c>null</c> otherwise.</param>
/// <param name="FailureReason">On failure, an actionable reason; <c>null</c> on success.</param>
/// <param name="Warning">On success, an Agent-authored caveat about the archive — set when a running world could not
/// be saved first, so its most recent changes may be missing (#377); <c>null</c> otherwise.</param>
public sealed record ServerBackupOutcome(
    bool Succeeded,
    string? ArchiveName,
    long SizeBytes,
    string? Sha256,
    DateTimeOffset? CreatedAt,
    string? FailureReason,
    string? Warning = null);

/// <summary>The result of deleting a backup archive host-side (F24).</summary>
/// <param name="Succeeded">Whether the archive was removed (or was already absent — deletion is idempotent).</param>
/// <param name="FailureReason">On failure, an actionable reason; <c>null</c> on success.</param>
public sealed record ServerBackupDeletionOutcome(bool Succeeded, string? FailureReason);

/// <summary>
/// Produces one backup of a Server's world data (F24): resolves the host paths from the Agent's configuration,
/// names a timestamped archive, and writes it through the <see cref="IBackupArchiver"/>. The source is the Server's
/// <c>/pz/data</c> world tree (<c>&lt;DataMountRoot&gt;/&lt;serverId&gt;</c>); the destination is
/// <c>&lt;BackupRoot&gt;/&lt;serverId&gt;/</c>. The SteamCMD install (a host sibling, <c>&lt;root&gt;/&lt;serverId&gt;.server</c>)
/// is never part of the source, so it is excluded by construction (ADR 0028). A running Server is asked to save its
/// world first (<see cref="IWorldSaver"/>, #377); when that fails the archive is still written, with a warning.
/// </summary>
public interface IServerBackupRunner
{
    /// <summary>Runs a backup for <paramref name="serverId"/> under <paramref name="operationId"/> and returns the
    /// terminal outcome. The <paramref name="operationId"/> makes the archive name unique and traceable to its
    /// Operation.</summary>
    Task<ServerBackupOutcome> RunAsync(ServerId serverId, OperationId operationId, CancellationToken cancellationToken);

    /// <summary>Takes the automatic backup a world-changing command asks for first (#379): the same save-first archive
    /// as <see cref="RunAsync"/>, named with a <c>-pre-op</c> marker. Returns <c>null</c> when the Server has no data
    /// directory yet — there is no world to protect, so the command runs without a backup.</summary>
    Task<ServerBackupOutcome?> RunPreOperationAsync(ServerId serverId, OperationId operationId, CancellationToken cancellationToken);

    /// <summary>Deletes the named backup archive from <paramref name="serverId"/>'s backup directory under the
    /// Agent's <c>BackupRoot</c> (F24). The <paramref name="archiveName"/> must be a bare file name — a name with a
    /// directory separator or <c>..</c> is refused (path-traversal guard). An already-absent archive is a success
    /// (idempotent), so a redelivered or double delete never errors.</summary>
    Task<ServerBackupDeletionOutcome> DeleteAsync(ServerId serverId, string archiveName, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IServerBackupRunner" />
public sealed partial class ServerBackupRunner : IServerBackupRunner
{
    private readonly IBackupArchiver _archiver;
    private readonly IWorldSaver _saver;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServerBackupRunner> _logger;

    public ServerBackupRunner(
        IBackupArchiver archiver,
        IWorldSaver saver,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<ServerBackupRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(archiver);
        ArgumentNullException.ThrowIfNull(saver);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _archiver = archiver;
        _saver = saver;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServerBackupOutcome> RunAsync(ServerId serverId, OperationId operationId, CancellationToken cancellationToken)
    {
        string sourceDirectory = Path.Combine(_options.DataMountRoot, serverId.ToString());
        if (!Directory.Exists(sourceDirectory))
        {
            // No world tree to copy — the Server was never provisioned/started on this host.
            LogNoWorldData(serverId);
            return new ServerBackupOutcome(
                false, null, 0, null, null,
                "This server has no data directory on its host to back up. Provision and start the server first.");
        }

        return await ArchiveAsync(serverId, sourceDirectory, operationId, preOperation: false, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ServerBackupOutcome?> RunPreOperationAsync(
        ServerId serverId, OperationId operationId, CancellationToken cancellationToken)
    {
        string sourceDirectory = Path.Combine(_options.DataMountRoot, serverId.ToString());
        if (!Directory.Exists(sourceDirectory))
        {
            // Never started on this host: no world to protect, so the command goes ahead without a backup.
            LogNoWorldToProtect(serverId);
            return null;
        }

        return await ArchiveAsync(serverId, sourceDirectory, operationId, preOperation: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ServerBackupOutcome> ArchiveAsync(
        ServerId serverId, string sourceDirectory, OperationId operationId, bool preOperation, CancellationToken cancellationToken)
    {
        // A running world is saved first so the archive holds its latest state (#377). A save that can't be sent
        // doesn't stop the backup — an older world is better than none — but the backup says so.
        WorldSaveResult save = await _saver.SaveAsync(serverId, cancellationToken).ConfigureAwait(false);
        string? warning = save.Status == WorldSaveStatus.Failed
            ? $"The world could not be saved before this backup ({save.FailureReason}), so the most recent changes may be missing."
            : null;

        DateTimeOffset createdAt = _timeProvider.GetUtcNow();
        string destinationDirectory = Path.Combine(_options.BackupRoot, serverId.ToString());
        string archiveName = ArchiveName(createdAt, operationId, preOperation);

        try
        {
            BackupArchiveResult result = _archiver.Create(sourceDirectory, destinationDirectory, archiveName, cancellationToken);
            LogBackupSucceeded(serverId, archiveName, result.SizeBytes);
            return new ServerBackupOutcome(true, archiveName, result.SizeBytes, result.Sha256, createdAt, null, warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogBackupFailed(serverId, ex);
            return new ServerBackupOutcome(false, null, 0, null, null, $"The backup could not be written: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public Task<ServerBackupDeletionOutcome> DeleteAsync(
        ServerId serverId, string archiveName, CancellationToken cancellationToken)
    {
        // Only a bare file name may be deleted — never a path. This refuses any traversal (separators, "..") even
        // though the name originates from a record the Agent itself authored (defense in depth).
        if (string.IsNullOrWhiteSpace(archiveName) || !string.Equals(Path.GetFileName(archiveName), archiveName, StringComparison.Ordinal))
        {
            LogDeleteRejected(serverId);
            return Task.FromResult(new ServerBackupDeletionOutcome(false, "The backup archive name is not a bare file name."));
        }

        string path = Path.Combine(_options.BackupRoot, serverId.ToString(), archiveName);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            LogBackupDeleted(serverId, archiveName);
            return Task.FromResult(new ServerBackupDeletionOutcome(true, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(serverId, ex);
            return Task.FromResult(new ServerBackupDeletionOutcome(false, $"The backup archive could not be deleted: {ex.Message}"));
        }
    }

    // A human-legible, per-Server-unique name: the UTC timestamp for reading, the OperationId for uniqueness and
    // traceability back to the Operation that produced it. Two backups in the same second never collide. An automatic
    // backup (#379) carries a "-pre-op" marker, as restore's protective one carries "-pre-restore".
    private static string ArchiveName(DateTimeOffset createdAt, OperationId operationId, bool preOperation) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"world-{createdAt.UtcDateTime:yyyyMMdd-HHmmss}-{operationId}{(preOperation ? "-pre-op" : string.Empty)}.tar.gz");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup for server {ServerId} found no data directory to archive.")]
    private partial void LogNoWorldData(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Automatic backup for server {ServerId} skipped: it has no world yet.")]
    private partial void LogNoWorldToProtect(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup for server {ServerId} wrote {ArchiveName} ({SizeBytes} bytes).")]
    private partial void LogBackupSucceeded(ServerId serverId, string archiveName, long sizeBytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup for server {ServerId} failed to write its archive.")]
    private partial void LogBackupFailed(ServerId serverId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup delete for server {ServerId} refused a non-bare archive name.")]
    private partial void LogDeleteRejected(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Backup delete for server {ServerId} removed {ArchiveName}.")]
    private partial void LogBackupDeleted(ServerId serverId, string archiveName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Backup delete for server {ServerId} failed to remove its archive.")]
    private partial void LogDeleteFailed(ServerId serverId, Exception exception);
}
