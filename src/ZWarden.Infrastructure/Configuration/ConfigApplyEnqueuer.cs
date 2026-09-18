using ZWarden.Application.Audit;
using ZWarden.Application.Configuration;
using ZWarden.Application.Operations;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;

namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// The shared enqueue half of a configuration-apply Operation, reused by the F20b
/// <see cref="ServerConfigurationEditor"/> (config edits and restores) and F22's mod manager (a mod change is a
/// <c>WorkshopItems=</c>/<c>Mods=</c> list-value config edit). It captures the drift baseline — the last recorded
/// revision's hash for the file, or <c>null</c> for the first write (ADR 0011) — size-checks the command payload,
/// enqueues a <b>mutating, server-scoped</b> Operation (per-server lock, ADR 0022), and audits. The caller has
/// already resolved and authorized the Server, and supplies the audit action + subject so the trail names its own
/// intent (e.g. a config apply vs a mod enable). Result is a <see cref="ServerConfigurationResult"/>; the mod
/// manager maps it to its own vocabulary.
/// </summary>
internal sealed class ConfigApplyEnqueuer
{
    private readonly IOperationCoordinator _operations;
    private readonly ConfigurationRevisionRepository _revisions;
    private readonly IAuditWriter _audit;

    public ConfigApplyEnqueuer(
        IOperationCoordinator operations,
        ConfigurationRevisionRepository revisions,
        IAuditWriter audit)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(audit);
        _operations = operations;
        _revisions = revisions;
        _audit = audit;
    }

    public async Task<ServerConfigurationResult> EnqueueAsync(
        UserId user,
        Server resolved,
        PzConfigFile file,
        IReadOnlyList<ConfigApplyEdit> edits,
        string auditAction,
        string auditSubject,
        string? expectedBaselineHash,
        CancellationToken cancellationToken)
    {
        // The drift baseline the Agent re-checks (ADR 0011). When the caller supplies one — the interactive
        // editor's live-read baseline (F20c, ADR 0042) — it wins, so the write is checked against the state the
        // operator actually saw. Otherwise fall back to the last recorded revision's hash, or null when none
        // exists (the Agent then treats it as the first write — no baseline to drift from).
        string? baselineHash = expectedBaselineHash;
        if (baselineHash is null)
        {
            ConfigurationRevision? baseline = await _revisions.FindLatestAsync(resolved.Id, file, cancellationToken).ConfigureAwait(false);
            baselineHash = baseline?.SnapshotHash;
        }

        string payload = new ConfigApplyPayload(file, baselineHash, edits).ToJson();
        if (payload.Length > Operation.MaxCommandPayloadLength)
        {
            return ServerConfigurationResult.Denied(
                ServerConfigurationFailure.InvalidInput, "Too many edits to apply in one operation; apply fewer at a time.");
        }

        try
        {
            // A mutating, server-scoped Operation on the Server's Agent (ADR 0022). Each request is a fresh
            // intent, so the idempotency key is fresh; the per-server lock refuses a second in-flight mutation.
            Operation operation = await _operations.EnqueueAsync(
                new EnqueueOperationRequest(
                    resolved.AgentId, OperationKind.ConfigApply, IsMutating: true, Guid.NewGuid().ToString("N"),
                    ServerId: resolved.Id, CommandPayload: payload),
                user,
                cancellationToken).ConfigureAwait(false);

            await _audit.WriteAsync(
                new AuditEntry(auditAction, AuditOutcome.Succeeded, user, resolved.Id, $"{auditSubject} — operation {operation.Id}"),
                cancellationToken).ConfigureAwait(false);

            return ServerConfigurationResult.Success(operation.Id);
        }
        catch (ServerBusyException)
        {
            // The per-server lock (ADR 0022) refused: another mutating Operation is already in flight.
            await _audit.WriteAsync(
                new AuditEntry(auditAction, AuditOutcome.Failed, user, resolved.Id, "server busy"),
                cancellationToken).ConfigureAwait(false);
            return ServerConfigurationResult.Denied(ServerConfigurationFailure.ServerBusy);
        }
    }
}
