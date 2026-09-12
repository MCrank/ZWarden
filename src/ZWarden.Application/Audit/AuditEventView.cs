using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Audit;

/// <summary>
/// A read projection of an <see cref="AuditEvent"/> for the administrative viewer (F6). It mirrors the
/// entity's non-secret fields; the Web layer never touches the EF entity directly.
/// </summary>
public sealed record AuditEventView(
    AuditEventId Id,
    DateTimeOffset OccurredAt,
    string Action,
    AuditOutcome Outcome,
    UserId? ActorUserId,
    ServerId? ServerId,
    string? CorrelationId,
    string? Detail);
