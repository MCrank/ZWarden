using ZWarden.Domain.Ids;

namespace ZWarden.Application.Backups;

/// <summary>Why a restore request was refused (F25). Fail-closed: the service re-checks the server-scoped
/// <c>Backup.Restore</c> permission and resolves the backup through the tenant filter before enqueueing, and refuses
/// early when the Server is (last observed) running.</summary>
public enum RestoreRequestFailure
{
    /// <summary>The caller lacks <c>Backup.Restore</c> on the backup's Server.</summary>
    NotAuthorized,

    /// <summary>No such backup in the current tenant (or already deleted).</summary>
    BackupNotFound,

    /// <summary>A conflicting mutating Operation is already in flight against this Server (per-server lock,
    /// ADR 0022) — a restore is a mutating Operation and cannot start while one is running.</summary>
    ServerBusy,

    /// <summary>The Server was last observed running. A restore overwrites the world tree, so the operator must stop
    /// the Server first — this is an advisory pre-check (last-observed state, trust-boundaries §3); the Agent makes
    /// the authoritative refusal if the container is genuinely running.</summary>
    ServerRunning,
}

/// <summary>The outcome of a restore request (F25): on success, the enqueued Operation whose state the caller polls;
/// otherwise a typed failure. The protective backup and the world swap happen inside that one Operation on the
/// Agent (ADR 0029); its completion records the protective backup.</summary>
public sealed record RestoreRequestResult(bool Succeeded, OperationId? Operation, RestoreRequestFailure? Failure)
{
    public static RestoreRequestResult Success(OperationId operation) => new(true, operation, null);

    public static RestoreRequestResult Denied(RestoreRequestFailure failure) => new(false, null, failure);
}
