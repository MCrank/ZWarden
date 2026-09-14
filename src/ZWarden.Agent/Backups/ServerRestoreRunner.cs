using System.Globalization;
using System.Security.Cryptography;
using Docker.DotNet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Docker;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Backups;

/// <summary>The result of a restore the Agent performed host-side (F25).</summary>
/// <param name="Succeeded">Whether the world was restored from the archive.</param>
/// <param name="RestoredArchiveName">On success, the backup archive that was restored; <c>null</c> otherwise.</param>
/// <param name="ProtectiveArchiveName">On success, the protective backup the Agent took of the pre-restore world
/// (its relative locator); <c>null</c> otherwise.</param>
/// <param name="ProtectiveSizeBytes">On success, the protective backup's byte size; <c>0</c> otherwise.</param>
/// <param name="ProtectiveSha256">On success, the protective backup's lowercase-hex SHA-256; <c>null</c> otherwise.</param>
/// <param name="ProtectiveCreatedAt">On success, when the protective backup was written (UTC); <c>null</c> otherwise.</param>
/// <param name="FailureReason">On failure, an actionable, non-secret reason; <c>null</c> on success.</param>
public sealed record ServerRestoreOutcome(
    bool Succeeded,
    string? RestoredArchiveName,
    string? ProtectiveArchiveName,
    long ProtectiveSizeBytes,
    string? ProtectiveSha256,
    DateTimeOffset? ProtectiveCreatedAt,
    string? FailureReason);

/// <summary>
/// Restores a Server's world data from one of its backups (F25, ADR 0029). The whole sequence is host-side I/O over
/// directories the Agent owns — no <c>exec</c>, no <c>docker cp</c> (ADR 0008). In order: it re-verifies the archive
/// against the recorded SHA-256 (a corrupt archive is <b>refused</b> before anything is touched), refuses if the
/// Server's container is running, takes an inline <b>protective</b> backup of the current world (so a mistaken
/// restore can itself be undone), unpacks the archive into a <b>staging</b> tree beside the world (never onto the
/// live tree), verifies the extraction produced world data, then <b>atomically swaps</b> the staging tree into place
/// — moving the current world aside first and rolling it back on any failure, so a failed restore never leaves a
/// half-written world.
/// </summary>
public interface IServerRestoreRunner
{
    /// <summary>Restores <paramref name="serverId"/>'s world from the backup named <paramref name="archiveName"/>
    /// under <paramref name="operationId"/>, re-verifying it against <paramref name="expectedSha256"/> first, and
    /// returns the terminal outcome. A long unpack reports interim progress through <paramref name="progress"/>.</summary>
    Task<ServerRestoreOutcome> RunAsync(
        ServerId serverId,
        OperationId operationId,
        string archiveName,
        string expectedSha256,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken);
}

/// <inheritdoc cref="IServerRestoreRunner" />
public sealed partial class ServerRestoreRunner : IServerRestoreRunner
{
    private readonly IContainerRuntime _containerRuntime;
    private readonly IBackupArchiver _archiver;
    private readonly IRestoreArchiveExtractor _extractor;
    private readonly AgentOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ServerRestoreRunner> _logger;

    public ServerRestoreRunner(
        IContainerRuntime containerRuntime,
        IBackupArchiver archiver,
        IRestoreArchiveExtractor extractor,
        IOptions<AgentOptions> options,
        TimeProvider timeProvider,
        ILogger<ServerRestoreRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(containerRuntime);
        ArgumentNullException.ThrowIfNull(archiver);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _containerRuntime = containerRuntime;
        _archiver = archiver;
        _extractor = extractor;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ServerRestoreOutcome> RunAsync(
        ServerId serverId,
        OperationId operationId,
        string archiveName,
        string expectedSha256,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        // Only a bare file name may be restored — never a path. Refuses any traversal (separators, "..") even though
        // the name originates from a record the control plane holds (defense in depth; mirrors the delete guard).
        if (string.IsNullOrWhiteSpace(archiveName)
            || !string.Equals(Path.GetFileName(archiveName), archiveName, StringComparison.Ordinal))
        {
            LogRestoreRejected(serverId);
            return Failure("The backup archive name is not a bare file name.");
        }

        string backupDirectory = Path.Combine(_options.BackupRoot, serverId.ToString());
        string archivePath = Path.Combine(backupDirectory, archiveName);
        string worldDirectory = Path.Combine(_options.DataMountRoot, serverId.ToString());

        if (!File.Exists(archivePath))
        {
            LogArchiveMissing(serverId, archiveName);
            return Failure("The backup archive to restore was not found on the server's host.");
        }

        // Verify the archive against the checksum the control plane recorded (F24). A corrupt or altered archive is
        // refused here, before the current world is touched (ADR 0028/0029 — restore validation, archive safety).
        await progress.ReportAsync(operationId, 5, "Verifying the backup archive…", cancellationToken).ConfigureAwait(false);
        string actualSha256;
        try
        {
            actualSha256 = ComputeSha256(archivePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogRestoreFailed(serverId, ex);
            return Failure($"The backup archive could not be read to verify it: {ex.Message}");
        }

        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            LogChecksumMismatch(serverId, archiveName);
            return Failure("The backup archive failed its integrity check (checksum mismatch); the restore was refused.");
        }

        // Refuse while the container is running: a live server is writing the world, and an atomic swap under it
        // would corrupt both trees. The stopped precondition is authoritative here (the Agent owns ground truth).
        try
        {
            if (await IsContainerRunningAsync(serverId, cancellationToken).ConfigureAwait(false))
            {
                LogRestoreWhileRunning(serverId);
                return Failure("The server is running. Stop the server before restoring a backup.");
            }
        }
        catch (DockerApiException ex)
        {
            LogRunStateUnknown(serverId, ex);
            return Failure($"Could not verify the server is stopped before restoring (HTTP {(int)ex.StatusCode}).");
        }

        DateTimeOffset createdAt = _timeProvider.GetUtcNow();
        string protectiveName = ProtectiveArchiveName(createdAt, operationId);
        string stagingDirectory = Path.Combine(_options.DataMountRoot, $"{serverId}.restore-{Guid.NewGuid():N}");
        string asideDirectory = Path.Combine(_options.DataMountRoot, $"{serverId}.pre-restore-{Guid.NewGuid():N}");

        try
        {
            // Protective backup of the current world before the destructive swap (ADR 0029). The world directory is
            // ensured so a never-started Server yields an empty protective archive rather than failing the restore.
            await progress.ReportAsync(operationId, 25, "Backing up the current world…", cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(worldDirectory);
            BackupArchiveResult protective = _archiver.Create(worldDirectory, backupDirectory, protectiveName, cancellationToken);

            // Unpack into staging beside the world — never onto the live tree. The extractor enforces archive safety
            // (rejects traversal, links, and non-file entries).
            await progress.ReportAsync(operationId, 55, "Unpacking the backup…", cancellationToken).ConfigureAwait(false);
            RestoreExtractionResult extraction = _extractor.Extract(archivePath, stagingDirectory, cancellationToken);

            // Health verification: a real world backup always has files. Refuse to swap an empty tree over a live
            // world — the protective backup is kept, and the current world is left untouched.
            if (extraction.FileCount == 0)
            {
                TryDeleteDirectory(stagingDirectory);
                LogEmptyArchive(serverId, archiveName);
                return Failure("The backup archive contained no world data; the restore was refused.");
            }

            // Atomic swap: move the current world aside, then move staging into its place. Both are on the same
            // volume (under DataMountRoot), so each move is a rename — no half-copied world can ever be observed.
            await progress.ReportAsync(operationId, 85, "Swapping in the restored world…", cancellationToken).ConfigureAwait(false);
            if (Directory.Exists(worldDirectory))
            {
                Directory.Move(worldDirectory, asideDirectory);
            }

            Directory.Move(stagingDirectory, worldDirectory);

            // Post-swap health check: the swapped-in world must be populated. If not, roll back to the pre-restore
            // world (kept in the aside copy) and refuse.
            if (!HasAnyFile(worldDirectory))
            {
                TryDeleteDirectory(worldDirectory);
                if (Directory.Exists(asideDirectory))
                {
                    Directory.Move(asideDirectory, worldDirectory);
                }

                LogRestoreUnverified(serverId);
                return Failure("The restored world did not verify after the swap; the previous world was kept.");
            }

            // Success: the protective archive is durable under BackupRoot, so the aside copy can be discarded.
            TryDeleteDirectory(asideDirectory);
            await progress.ReportAsync(operationId, 100, "Restore complete.", cancellationToken).ConfigureAwait(false);
            LogRestoreSucceeded(serverId, archiveName, protectiveName);
            return new ServerRestoreOutcome(
                true, archiveName, protectiveName, protective.SizeBytes, protective.Sha256, createdAt, null);
        }
        catch (RestoreArchiveException ex)
        {
            // Unsafe archive content — extraction refused it before any swap, so the live world is untouched.
            TryDeleteDirectory(stagingDirectory);
            LogRestoreRefusedUnsafe(serverId, ex);
            return Failure($"The backup archive was rejected as unsafe: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort rollback: if the world was moved aside but the staging move failed, restore the world.
            RollBack(worldDirectory, asideDirectory);
            TryDeleteDirectory(stagingDirectory);
            LogRestoreFailed(serverId, ex);
            return Failure($"The restore could not be completed: {ex.Message}");
        }
    }

    private async Task<bool> IsContainerRunningAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedContainer> managed =
            await _containerRuntime.ListManagedAsync(cancellationToken).ConfigureAwait(false);
        return managed.Any(container =>
            container.ServerId == serverId
            && container.State.Equals("running", StringComparison.OrdinalIgnoreCase));
    }

    private static void RollBack(string worldDirectory, string asideDirectory)
    {
        try
        {
            if (!Directory.Exists(worldDirectory) && Directory.Exists(asideDirectory))
            {
                Directory.Move(asideDirectory, worldDirectory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A rollback that itself fails leaves the pre-restore world in its aside directory; the failed
            // Operation's reason tells the operator, and the protective backup remains restorable.
        }
    }

    private static bool HasAnyFile(string directory) =>
        Directory.Exists(directory)
        && Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any();

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // The protective backup's name mirrors a normal backup's (F24) with a "-pre-restore" marker, so an operator
    // reading the backup list can see it was taken automatically before a restore.
    private static string ProtectiveArchiveName(DateTimeOffset createdAt, OperationId operationId) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"world-{createdAt.UtcDateTime:yyyyMMdd-HHmmss}-{operationId}-pre-restore.tar.gz");

    private static ServerRestoreOutcome Failure(string reason) => new(false, null, null, 0, null, null, reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore for server {ServerId} refused a non-bare archive name.")]
    private partial void LogRestoreRejected(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore for server {ServerId} could not find archive {ArchiveName}.")]
    private partial void LogArchiveMissing(ServerId serverId, string archiveName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore for server {ServerId} refused archive {ArchiveName}: checksum mismatch.")]
    private partial void LogChecksumMismatch(ServerId serverId, string archiveName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore for server {ServerId} refused: the server is running.")]
    private partial void LogRestoreWhileRunning(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore for server {ServerId} could not verify the container run state.")]
    private partial void LogRunStateUnknown(ServerId serverId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore for server {ServerId} refused archive {ArchiveName}: no world data.")]
    private partial void LogEmptyArchive(ServerId serverId, string archiveName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore for server {ServerId} did not verify after the swap; the previous world was kept.")]
    private partial void LogRestoreUnverified(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore for server {ServerId} refused an unsafe archive.")]
    private partial void LogRestoreRefusedUnsafe(ServerId serverId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Restore for server {ServerId} failed to complete.")]
    private partial void LogRestoreFailed(ServerId serverId, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Restore for server {ServerId} restored {ArchiveName} (protective backup {ProtectiveName}).")]
    private partial void LogRestoreSucceeded(ServerId serverId, string archiveName, string protectiveName);
}
