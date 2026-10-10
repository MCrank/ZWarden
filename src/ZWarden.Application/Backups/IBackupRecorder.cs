using ZWarden.Domain.Ids;

namespace ZWarden.Application.Backups;

/// <summary>
/// The ingest seam that persists a backup Operation's confirmed outcome (F24), called from the hub when an
/// <c>OperationCompleted</c> arrives (the pattern F14/F17 use for provisioning/build ingest). It takes the
/// primitive facts the Agent observed — never the Contracts wire types — so the Application/Infrastructure layers
/// stay free of the protocol assembly. Both methods are tenant-scoped and ownership-checked: a report for a Server
/// or Agent the current tenant does not own is a no-op (trust-boundaries §3).
/// </summary>
public interface IBackupRecorder
{
    /// <summary>Records a completed backup: creates the tenant-owned <c>Backup</c> from the reported archive facts,
    /// reading its retention reason from the Operation's stored command payload. No-op if the Server is not visible
    /// or is not owned by the reporting Agent. The optional <paramref name="warning"/> is the Agent's caveat about the
    /// archive (#377), stored with it.</summary>
    Task RecordCreatedAsync(
        ServerId serverId,
        AgentId agentId,
        OperationId operationId,
        string archiveName,
        long sizeBytes,
        string sha256,
        DateTimeOffset createdAt,
        string? warning = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the backup record a confirmed deletion Operation removed the archive for, resolving the
    /// record id from the Operation's stored command payload. No-op if the Operation or record is not visible.</summary>
    Task RecordDeletedAsync(OperationId operationId, CancellationToken cancellationToken = default);

    /// <summary>Records an <b>automatic</b> backup the Agent took inside another Operation: a restore's protective
    /// backup of the pre-restore world (F25, ADR 0029), or the backup a config apply, mod update or game update took
    /// first (#379). Creates a tenant-owned <c>Backup</c> tagged <see cref="Domain.Backups.BackupReason.PreOperation"/>
    /// from the reported archive facts, so the change can be rolled back, audits it as taken by the system for
    /// <paramref name="operationId"/>, and runs retention (<see cref="IBackupRetention"/>). Returns whether it was
    /// recorded: <c>false</c> (a no-op) if the Server is not visible or is not owned by the reporting Agent (the same
    /// ownership guard as <see cref="RecordCreatedAsync"/>).</summary>
    Task<bool> RecordPreOperationBackupAsync(
        ServerId serverId,
        AgentId agentId,
        OperationId operationId,
        string archiveName,
        long sizeBytes,
        string sha256,
        DateTimeOffset createdAt,
        string? warning = null,
        CancellationToken cancellationToken = default);
}
