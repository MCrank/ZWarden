using ZWarden.Domain.Ids;

namespace ZWarden.Application.Players;

/// <summary>Why a player-management action was refused (F19). Fail-closed: the service resolves the Server
/// through the tenant filter and re-checks the server-scoped permission before enqueuing anything, and validates
/// the operator's arguments before they can reach RCON.</summary>
public enum PlayerManagementFailure
{
    /// <summary>The caller lacks the matching server-scoped permission on this Server.</summary>
    NotAuthorized,

    /// <summary>No such Server in the current tenant (or not visible to this caller's tenant).</summary>
    ServerNotFound,

    /// <summary>The supplied username or reason failed validation (F19 D-3) — it could carry an RCON command
    /// injection, so it is rejected before an Operation is enqueued.</summary>
    InvalidInput,
}

/// <summary>The outcome of a player-management action (F19): on success, the enqueued (non-mutating) Operation
/// whose state the caller polls; otherwise a typed failure. Being non-mutating, a player action never contends
/// for the per-server lock, so there is no <c>ServerBusy</c> case.</summary>
public sealed record PlayerManagementResult(bool Succeeded, OperationId? Operation, PlayerManagementFailure? Failure, string? Detail = null)
{
    public static PlayerManagementResult Success(OperationId operation) => new(true, operation, null);

    public static PlayerManagementResult Denied(PlayerManagementFailure failure, string? detail = null) =>
        new(false, null, failure, detail);
}
