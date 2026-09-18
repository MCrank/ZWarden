using ZWarden.Domain.Ids;

namespace ZWarden.Application.Configuration;

/// <summary>Why a configuration apply was refused (F20b). Fail-closed: the service re-checks the server-scoped
/// <c>ServerConfigurationEdit</c> permission and resolves the Server through the tenant filter before enqueueing
/// anything.</summary>
public enum ServerConfigurationFailure
{
    /// <summary>The caller lacks <c>ServerConfigurationEdit</c> on this Server.</summary>
    NotAuthorized,

    /// <summary>No such Server in the current tenant (or not visible to this caller's tenant).</summary>
    ServerNotFound,

    /// <summary>A conflicting mutating Operation is already in flight against this Server (per-server lock,
    /// ADR 0022) — a lifecycle or config action has not finished. A config write never runs concurrently with
    /// another mutation.</summary>
    ServerBusy,

    /// <summary>The requested edits are not valid to enqueue — empty, an empty path, or too many to fit one
    /// Operation's command payload. The Agent re-validates the values before it writes.</summary>
    InvalidInput,

    /// <summary>The Server's owning Agent is not connected, so an operator-authored whole-file edit could not be
    /// staged to it (F20c PR-D, ADR 0042). Only the raw-edit path reports this — a surgical apply queues while the
    /// Agent is offline; a raw edit cannot, because its text rides the live connection ahead of the Operation.</summary>
    AgentOffline,
}

/// <summary>The outcome of a configuration apply (F20b): on success, the enqueued mutating Operation whose state
/// the caller polls (the Agent drift-checks and writes, then reports a revision); otherwise a typed failure with
/// an optional operator-facing message.</summary>
public sealed record ServerConfigurationResult(
    bool Succeeded, OperationId? Operation, ServerConfigurationFailure? Failure, string? Message = null)
{
    public static ServerConfigurationResult Success(OperationId operation) => new(true, operation, null);

    public static ServerConfigurationResult Denied(ServerConfigurationFailure failure, string? message = null) =>
        new(false, null, failure, message);
}
