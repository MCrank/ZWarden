using Microsoft.Extensions.Options;
using ZWarden.Application.Audit;
using ZWarden.Application.Operations;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// The tenant-scoped read and Agent-event ingest for Operations (F11). Ingest is idempotent and defensive:
/// an event for an unknown or already-terminal Operation is a no-op (redelivered completion, late progress),
/// which is what makes the Agent's at-least-once reporting safe (PRD 20). Agent text is untrusted — the
/// domain truncates it; nothing here interprets it (trust-boundaries.md §3).
/// </summary>
public sealed class OperationStore : IOperationStore
{
    private readonly ZWardenDbContext _context;
    private readonly OperationRepository _operations;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;
    private readonly OperationEngineOptions _options;

    public OperationStore(
        ZWardenDbContext context,
        OperationRepository operations,
        IAuditWriter audit,
        TimeProvider clock,
        IOptions<OperationEngineOptions> options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        _context = context;
        _operations = operations;
        _audit = audit;
        _clock = clock;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<Operation?> FindAsync(OperationId operationId, CancellationToken cancellationToken = default)
        => _operations.FindByIdAsync(operationId, cancellationToken);

    /// <inheritdoc />
    public async Task ApplyProgressAsync(
        OperationId operationId,
        int percentComplete,
        string? statusLine,
        CancellationToken cancellationToken = default)
    {
        Operation? op = await _operations.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (op is null || op.IsTerminal)
        {
            return;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        op.ReportProgress(percentComplete, statusLine, now + _options.LeaseDuration, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CompleteSucceededAsync(OperationId operationId, CancellationToken cancellationToken = default)
    {
        Operation? op = await _operations.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (op is null || op.IsTerminal)
        {
            return;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        op.Succeed(now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(OperationAuditActions.Succeeded, AuditOutcome.Succeeded, ServerId: op.ServerId, Detail: Describe(op)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CompleteFailedAsync(
        OperationId operationId,
        string? failureReason,
        CancellationToken cancellationToken = default)
    {
        Operation? op = await _operations.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (op is null || op.IsTerminal)
        {
            return;
        }

        DateTimeOffset now = _clock.GetUtcNow();

        // An Agent aborting a cancellation-in-progress reports failure; record that as the honoured
        // cancellation it is, not a generic failure (ADR 0022). A failure while merely Running is a failure.
        if (op.State == OperationState.Cancelling)
        {
            op.Cancel(now);
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await _audit.WriteAsync(
                new AuditEntry(OperationAuditActions.Cancelled, AuditOutcome.Succeeded, ServerId: op.ServerId, Detail: Describe(op)),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        op.Fail(string.IsNullOrWhiteSpace(failureReason) ? "operation failed" : failureReason, now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await _audit.WriteAsync(
            new AuditEntry(OperationAuditActions.Failed, AuditOutcome.Failed, ServerId: op.ServerId, Detail: Describe(op)),
            cancellationToken).ConfigureAwait(false);
    }

    private static string Describe(Operation op) => $"{op.Kind} {op.Id}";
}
