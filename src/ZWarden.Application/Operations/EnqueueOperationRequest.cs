using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;

namespace ZWarden.Application.Operations;

/// <summary>
/// The inputs to enqueue an <see cref="Operation"/>. The caller (a feature acting under its own
/// permission) names the executing <see cref="AgentId"/>, what to run, whether it mutates, an
/// idempotency key, and — when server-scoped — the <see cref="ServerId"/>. A mutating Operation must be
/// server-scoped. The tenant is ambient (stamped on insert), never supplied here.
/// </summary>
/// <param name="AgentId">The Agent that will execute the Operation.</param>
/// <param name="Kind">What to run.</param>
/// <param name="IsMutating">Whether the Operation mutates the Server and so takes the per-server lock.</param>
/// <param name="IdempotencyKey">A key unique per logical intent; a duplicate enqueue returns the existing
/// Operation (PRD 20).</param>
/// <param name="ServerId">The Server acted on, or <c>null</c> for host-level work. Required when
/// <paramref name="IsMutating"/> is true.</param>
public sealed record EnqueueOperationRequest(
    AgentId AgentId,
    OperationKind Kind,
    bool IsMutating,
    string IdempotencyKey,
    ServerId? ServerId = null);
