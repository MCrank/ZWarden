using ZWarden.Domain.Audit;

namespace ZWarden.Web.Components.Pages.Audit;

/// <summary>
/// One row of the audit table (#161), projected by the static-SSR <c>/audit</c> page (which has the authorized,
/// tenant-filtered load and an <c>HttpContext</c>, so it can resolve actor/target ids to names) and handed to the
/// interactive <see cref="AuditTable"/> island as a parameter — BbDataGrid renders inside an interactive island,
/// not under pure static SSR. It must be a public, serializable type because it crosses the prerender→interactive
/// boundary. It carries only the non-secret projection the audit entity already exposes; the ids are resolved to
/// friendly display names for the operator, with the canonical typed id kept for the cell title / fallback. All
/// audit text is rendered as data.
/// </summary>
/// <param name="Time">The occurrence time, formatted UTC (<c>yyyy-MM-dd HH:mm:ss</c>).</param>
/// <param name="Actor">The actor's display name (email), or <c>null</c> for a system/no-actor event.</param>
/// <param name="ActorId">The actor's canonical id (<c>usr-</c>) for the cell title, or <c>null</c>.</param>
/// <param name="Action">The stable machine-readable action name (the audit currency).</param>
/// <param name="Detail">The optional short non-secret detail, or <c>null</c>.</param>
/// <param name="Target">The target Server's name, or <c>null</c> when the event is not server-scoped.</param>
/// <param name="TargetId">The target Server's canonical id (<c>srv-</c>) for the cell title, or <c>null</c>.</param>
/// <param name="Outcome">The outcome (drives the OutcomeBadge; Denied is visually distinct).</param>
/// <param name="Correlation">The correlation id tying related events together, or <c>null</c>.</param>
public sealed record AuditRow(
    string Time,
    string? Actor,
    string? ActorId,
    string Action,
    string? Detail,
    string? Target,
    string? TargetId,
    AuditOutcome Outcome,
    string? Correlation);
