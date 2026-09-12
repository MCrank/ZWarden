namespace ZWarden.Domain.Operations;

/// <summary>
/// What an <see cref="Operation"/> does. Stored by name (never its ordinal), so entries may be reordered
/// but not renumbered. F11 ships only <see cref="DiagnosticsPing"/>; the real mutating kinds
/// (<c>RestartServer</c>, <c>ApplyConfig</c>, <c>Backup</c>, …) land with their owning features (F13/F15/…),
/// each declaring its own mutating-ness at enqueue. Whether an Operation takes the per-server lock is
/// <see cref="Operation.IsMutating"/>, set at enqueue — not derived from this enum — so the lock is
/// testable before any mutating kind exists (ADR 0022).
/// </summary>
public enum OperationKind
{
    /// <summary>A non-mutating round-trip probe of a Server's Agent: dispatch a command and observe the
    /// reply. The first diagnostic tool for "is this Agent actually round-tripping commands right now?".
    /// Being non-mutating, it never claims the per-server lock.</summary>
    DiagnosticsPing = 0,
}
