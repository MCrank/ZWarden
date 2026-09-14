using ZWarden.Application.Backups;
using ZWarden.Application.Operations;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Backups;

/// <summary>
/// Persists a backup Operation's confirmed outcome (F24), called from the hub when an <c>OperationCompleted</c>
/// arrives (the F14/F17 ingest pattern). It is tenant-scoped by construction — every read builds on the tenant
/// filter — and ownership-checked: a create is recorded only against a Server the reporting Agent owns, so an Agent
/// can never plant a backup on another Agent's Server (trust-boundaries §3). The retention reason and the record id
/// are read from the Operation's stored command payload the enqueueing service wrote.
/// </summary>
public sealed class BackupRecorder : IBackupRecorder
{
    private readonly IOperationStore _operations;
    private readonly ServerRepository _servers;
    private readonly BackupRepository _backups;
    private readonly ZWardenDbContext _context;

    public BackupRecorder(
        IOperationStore operations,
        ServerRepository servers,
        BackupRepository backups,
        ZWardenDbContext context)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(context);
        _operations = operations;
        _servers = servers;
        _backups = backups;
        _context = context;
    }

    /// <inheritdoc />
    public async Task RecordCreatedAsync(
        ServerId serverId,
        AgentId agentId,
        OperationId operationId,
        string archiveName,
        long sizeBytes,
        string sha256,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        Server? server = await _servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (server is null || server.AgentId != agentId)
        {
            // A report for a Server this tenant does not own, or one this Agent does not own: no-op (§3).
            return;
        }

        BackupReason reason = await ResolveReasonAsync(operationId, cancellationToken).ConfigureAwait(false);
        _backups.Add(Backup.Record(serverId, agentId, archiveName, sizeBytes, sha256, reason, createdAt));
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RecordDeletedAsync(OperationId operationId, CancellationToken cancellationToken = default)
    {
        Operation? operation = await _operations.FindAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation?.CommandPayload is not { } payloadJson)
        {
            return;
        }

        BackupCommandPayload payload = BackupCommandPayload.FromJson(payloadJson);
        if (payload.BackupId is not { } raw || !BackupId.TryParse(raw, out BackupId backupId))
        {
            return;
        }

        Backup? backup = await _backups.FindByIdAsync(backupId, cancellationToken).ConfigureAwait(false);
        if (backup is null)
        {
            // Already gone (a double delete, or removed by another path): nothing to do.
            return;
        }

        _backups.Remove(backup);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // The retention reason the enqueueing service wrote onto the Operation's payload; defaults to Manual for a
    // payload that is missing or unreadable, so a backup is never dropped over a metadata gap.
    private async Task<BackupReason> ResolveReasonAsync(OperationId operationId, CancellationToken cancellationToken)
    {
        Operation? operation = await _operations.FindAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (operation?.CommandPayload is not { } payloadJson)
        {
            return BackupReason.Manual;
        }

        BackupCommandPayload payload = BackupCommandPayload.FromJson(payloadJson);
        return Enum.TryParse(payload.Reason, out BackupReason reason) ? reason : BackupReason.Manual;
    }
}
