using ZWarden.Domain.Ids;

namespace ZWarden.Application.Backups;

/// <summary>Why a backup request was refused (F24). Fail-closed: the service re-checks the server-scoped
/// <c>Backup.Create</c> permission and resolves the Server through the tenant filter before enqueueing.</summary>
public enum BackupRequestFailure
{
    /// <summary>The caller lacks <c>Backup.Create</c> on this Server.</summary>
    NotAuthorized,

    /// <summary>No such Server in the current tenant (or not visible to this caller's tenant).</summary>
    ServerNotFound,

    /// <summary>A conflicting mutating Operation is already in flight against this Server (per-server lock,
    /// ADR 0022) — a backup is a mutating Operation and cannot start while one is running.</summary>
    ServerBusy,
}

/// <summary>The outcome of a backup request (F24): on success, the enqueued Operation whose state the caller
/// polls; otherwise a typed failure.</summary>
public sealed record BackupRequestResult(bool Succeeded, OperationId? Operation, BackupRequestFailure? Failure)
{
    public static BackupRequestResult Success(OperationId operation) => new(true, operation, null);

    public static BackupRequestResult Denied(BackupRequestFailure failure) => new(false, null, failure);
}

/// <summary>Why a backup deletion was refused (F24). Fail-closed: the service re-checks <c>Backup.Delete</c> and
/// resolves the backup through the tenant filter before enqueueing.</summary>
public enum BackupDeletionFailure
{
    /// <summary>The caller lacks <c>Backup.Delete</c> on the backup's Server.</summary>
    NotAuthorized,

    /// <summary>No such backup in the current tenant (or already deleted).</summary>
    BackupNotFound,
}

/// <summary>The outcome of a backup deletion request (F24): on success, the enqueued deletion Operation whose state
/// the caller polls (the record is removed on its confirmed completion); otherwise a typed failure.</summary>
public sealed record BackupDeletionOutcome(bool Succeeded, OperationId? Operation, BackupDeletionFailure? Failure)
{
    public static BackupDeletionOutcome Success(OperationId operation) => new(true, operation, null);

    public static BackupDeletionOutcome Denied(BackupDeletionFailure failure) => new(false, null, failure);
}
