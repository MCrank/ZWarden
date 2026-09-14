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
    /// or is not owned by the reporting Agent.</summary>
    Task RecordCreatedAsync(
        ServerId serverId,
        AgentId agentId,
        OperationId operationId,
        string archiveName,
        long sizeBytes,
        string sha256,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default);

    /// <summary>Removes the backup record a confirmed deletion Operation removed the archive for, resolving the
    /// record id from the Operation's stored command payload. No-op if the Operation or record is not visible.</summary>
    Task RecordDeletedAsync(OperationId operationId, CancellationToken cancellationToken = default);
}
