using System.Globalization;
using Microsoft.Extensions.Options;
using ZWarden.Application.Audit;
using ZWarden.Application.Backups;
using ZWarden.Application.Operations;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Backups;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Backups;

/// <inheritdoc cref="IBackupRetention" />
public sealed class BackupRetention : IBackupRetention
{
    private readonly BackupRepository _backups;
    private readonly IOperationCoordinator _operations;
    private readonly IOperationStore _store;
    private readonly IAuditWriter _audit;
    private readonly BackupOptions _options;

    public BackupRetention(
        BackupRepository backups,
        IOperationCoordinator operations,
        IOperationStore store,
        IAuditWriter audit,
        IOptions<BackupOptions> options)
    {
        ArgumentNullException.ThrowIfNull(backups);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(options);
        _backups = backups;
        _operations = operations;
        _store = store;
        _audit = audit;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<int> PruneAsync(ServerId server, CancellationToken cancellationToken = default)
    {
        // Ordered in memory: SQLite can't ORDER BY a DateTimeOffset, and one server's backups are few.
        IReadOnlyList<Backup> all = await _backups.ListForServerAsync(server, cancellationToken).ConfigureAwait(false);
        List<Backup> excess = [.. all
            .Where(b => b.Reason == BackupReason.PreOperation)
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id.ToString(), StringComparer.Ordinal)
            .Skip(_options.KeepAutomatic)];
        if (excess.Count == 0)
        {
            return 0;
        }

        HashSet<string> beingDeleted = await BeingDeletedAsync(server, cancellationToken).ConfigureAwait(false);
        int started = 0;
        foreach (Backup backup in excess.Where(b => !beingDeleted.Contains(b.Id.ToString())))
        {
            Operation operation;
            try
            {
                // The same non-mutating Operation an operator's delete enqueues (F24): the Agent removes the archive and
                // the record goes on its confirmed completion. Acting as the system: retention is policy.
                operation = await _operations.EnqueueAsync(
                    new EnqueueOperationRequest(
                        backup.AgentId, OperationKind.DeleteBackup, IsMutating: false, Guid.NewGuid().ToString("N"),
                        ServerId: server,
                        CommandPayload: new BackupCommandPayload(BackupId: backup.Id.ToString(), ArchiveName: backup.ArchiveName).ToJson()),
                    actor: null,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (HostOfflineException)
            {
                // The host dropped mid-prune: the rest stay, and the next automatic backup prunes them.
                break;
            }

            await _audit.WriteAsync(
                new AuditEntry(
                    ServerAuditActions.BackupPruned, AuditOutcome.Succeeded, ActorUserId: null, server,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{backup.ArchiveName}: retention keeps the last {_options.KeepAutomatic} automatic backups, operation {operation.Id}")),
                cancellationToken).ConfigureAwait(false);
            started++;
        }

        return started;
    }

    // The backups an in-flight DeleteBackup Operation already targets, so a prune never deletes (or audits) one twice.
    private async Task<HashSet<string>> BeingDeletedAsync(ServerId server, CancellationToken cancellationToken)
    {
        IReadOnlyList<Operation> active = await _store.ListActiveAsync(cancellationToken).ConfigureAwait(false);
        return new HashSet<string>(
            active
                .Where(o => o.Kind == OperationKind.DeleteBackup && o.ServerId == server && o.CommandPayload is not null)
                .Select(o => BackupCommandPayload.FromJson(o.CommandPayload!).BackupId)
                .OfType<string>(),
            StringComparer.Ordinal);
    }
}
