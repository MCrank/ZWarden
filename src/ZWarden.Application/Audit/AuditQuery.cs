using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Audit;

/// <summary>
/// The filter over the audit trail for the administrative viewer (F6). Every field is optional; an unset
/// field does not constrain. Results are always tenant-scoped (ADR 0016) and ordered newest-first, and
/// <see cref="Skip"/>/<see cref="Take"/> page them.
/// </summary>
/// <param name="From">Only events at or after this instant (inclusive).</param>
/// <param name="To">Only events at or before this instant (inclusive).</param>
/// <param name="Action">Only events with this exact action name.</param>
/// <param name="ActorUserId">Only events caused by this user.</param>
/// <param name="ServerId">Only events concerning this Server.</param>
/// <param name="Outcome">Only events with this outcome.</param>
/// <param name="CorrelationId">Only events with this correlation id.</param>
/// <param name="Skip">How many matching events to skip (paging).</param>
/// <param name="Take">The page size; a non-positive value returns all matches.</param>
public sealed record AuditQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Action = null,
    UserId? ActorUserId = null,
    ServerId? ServerId = null,
    AuditOutcome? Outcome = null,
    string? CorrelationId = null,
    int Skip = 0,
    int Take = 50);
