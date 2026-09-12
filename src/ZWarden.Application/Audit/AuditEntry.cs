using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Audit;

/// <summary>
/// The non-secret inputs a caller supplies to record an audit event (F6; ADR 0019). The writer stamps the
/// rest — the ambient tenant, the clock's <c>OccurredAt</c>, and the correlation id — so a call site names
/// only what it knows. It carries no credential, exactly as the <c>AuthenticationEvent</c> it is often
/// mapped from already guarantees.
/// </summary>
/// <param name="Action">The stable machine-readable action name (e.g. <c>Role.Created</c>) — never ad-hoc.</param>
/// <param name="Outcome">The outcome of the occurrence.</param>
/// <param name="ActorUserId">The user who caused it, when known.</param>
/// <param name="ServerId">The Server it concerns, when server-scoped.</param>
/// <param name="Detail">An optional short, non-secret description; never a credential.</param>
public sealed record AuditEntry(
    string Action,
    AuditOutcome Outcome,
    UserId? ActorUserId = null,
    ServerId? ServerId = null,
    string? Detail = null);
