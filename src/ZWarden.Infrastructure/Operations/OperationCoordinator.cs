using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using ZWarden.Application.Audit;
using ZWarden.Application.Operations;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Operations;

/// <summary>
/// The enqueue and cancellation engine (F11). Enqueue creates the Operation and <b>acquires the per-server
/// lock as the same insert</b> (PRD 21) — a unique-constraint conflict becomes a typed
/// <see cref="ServerBusyException"/> — enforces enqueue idempotency (PRD 20), audits, and attempts an
/// initial dispatch. It commits the lock row and never holds a transaction across Agent work (ADR 0005
/// condition 5).
/// </summary>
public sealed class OperationCoordinator : IOperationCoordinator
{
    private readonly ZWardenDbContext _context;
    private readonly OperationRepository _operations;
    private readonly IOperationDispatcher _dispatcher;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public OperationCoordinator(
        ZWardenDbContext context,
        OperationRepository operations,
        IOperationDispatcher dispatcher,
        IAuditWriter audit,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        _context = context;
        _operations = operations;
        _dispatcher = dispatcher;
        _audit = audit;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Operation> EnqueueAsync(
        EnqueueOperationRequest request,
        UserId? actor = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Fast idempotency path: an existing Operation for this key wins without a write (PRD 20).
        Operation? existing = await _operations
            .FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        Operation op = Operation.Enqueue(
            request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, now, request.ServerId);
        _operations.Add(op);

        try
        {
            // The insert IS the lock acquire (ADR 0005/0022); the commit releases no transaction it holds.
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _context.Entry(op).State = EntityState.Detached;

            // The conflict is either the idempotency index (a concurrent enqueue of the same key) or the
            // per-server lock. Re-read by key: if it now exists, that race is the idempotent answer.
            Operation? raced = await _operations
                .FindByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (raced is not null)
            {
                return raced;
            }

            if (request.IsMutating && request.ServerId is { } serverId)
            {
                throw new ServerBusyException(serverId);
            }

            throw;
        }

        await _audit.WriteAsync(
            new AuditEntry(OperationAuditActions.Enqueued, AuditOutcome.Succeeded, actor, op.ServerId, Describe(op)),
            cancellationToken).ConfigureAwait(false);

        // Best-effort immediate dispatch; the dispatcher persists the Running transition before the command
        // leaves (avoiding the completed-before-Running race) and audits Started, or leaves it Pending if the
        // Agent is offline. In PR-A the default dispatcher is a no-op.
        await _dispatcher.TryDispatchAsync(op, cancellationToken).ConfigureAwait(false);
        return op;
    }

    /// <inheritdoc />
    public async Task<Operation> RequestCancellationAsync(
        OperationId operationId,
        UserId? actor = null,
        CancellationToken cancellationToken = default)
    {
        Operation op = await _operations.FindByIdAsync(operationId, cancellationToken).ConfigureAwait(false)
            ?? throw new OperationNotFoundException(operationId);

        // Cancelling a terminal Operation is a no-op (idempotent), not an error.
        if (op.IsTerminal)
        {
            return op;
        }

        DateTimeOffset now = _clock.GetUtcNow();
        op.RequestCancel(now);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // A Pending Operation cancels immediately; a Running one enters Cancelling, and its terminal Cancelled
        // audit is written when the Agent honours the cancel (ingest). The agent-cancel signal is PR-B.
        if (op.State == OperationState.Cancelled)
        {
            await _audit.WriteAsync(
                new AuditEntry(OperationAuditActions.Cancelled, AuditOutcome.Succeeded, actor, op.ServerId, Describe(op)),
                cancellationToken).ConfigureAwait(false);
        }

        return op;
    }

    private static string Describe(Operation op) => $"{op.Kind} {op.Id}";

    // The one place a provider difference survives (ADR 0005): both throw DbUpdateException on an insert
    // conflict, but the inner exception differs by provider.
    private static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode == SqliteConstraintErrorCode,
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
        _ => false,
    };

    // SQLITE_CONSTRAINT (19) covers both the PRIMARY KEY and UNIQUE extended codes.
    private const int SqliteConstraintErrorCode = 19;
}
