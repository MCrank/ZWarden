using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>Why a lifecycle action was refused (F15). Fail-closed: the service re-checks the server-scoped
/// permission and resolves the Server through the tenant filter before enqueueing anything.</summary>
public enum ServerLifecycleFailure
{
    /// <summary>The caller lacks the matching server-scoped permission on this Server.</summary>
    NotAuthorized,

    /// <summary>No such Server in the current tenant (or not visible to this caller's tenant).</summary>
    ServerNotFound,

    /// <summary>A conflicting mutating Operation is already in flight against this Server (per-server lock,
    /// ADR 0022) — the previous action has not finished.</summary>
    ServerBusy,

    /// <summary>The requested host game port is outside the allowed range (<c>HostPortRules</c>, #229).</summary>
    InvalidPort,

    /// <summary>The requested host port pair overlaps another Server's recorded pair on the same host (#229). The
    /// Agent re-checks against every container on the daemon; this is the fast control-plane refusal.</summary>
    PortInUse,
}

/// <summary>The outcome of a lifecycle action (F15): on success, the enqueued Operation whose state the caller
/// polls; otherwise a typed failure.</summary>
public sealed record ServerLifecycleResult(bool Succeeded, OperationId? Operation, ServerLifecycleFailure? Failure)
{
    public static ServerLifecycleResult Success(OperationId operation) => new(true, operation, null);

    public static ServerLifecycleResult Denied(ServerLifecycleFailure failure) => new(false, null, failure);
}
