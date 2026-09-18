using ZWarden.Domain.Servers;

namespace ZWarden.Web.Components.Pages.Servers;

/// <summary>
/// One row of the Fleet board (#158), projected by the static-SSR parent (which has the authorized, tenant-
/// filtered load and an <c>HttpContext</c>) and handed to the interactive <see cref="FleetBoard"/> island as a
/// parameter. It must be a public, serializable type because it crosses the prerender→interactive boundary.
/// It carries only the last-reported registry metadata plus a <b>point-in-time snapshot</b> of the ownership-
/// guarded live caches (CPU/memory) — the board does not refresh at 1&#160;Hz (a static snapshot, ADR 0040
/// posture); reopen or navigate to re-read. Players/uptime/tick have no v1.0 backing data and render as
/// <c>—</c> in the island. All Agent-reported text is untrusted and rendered as data (trust-boundaries §3/§8).
/// </summary>
/// <param name="Id">The Server's canonical id (<c>srv-</c>) as a string — the row-click target and cache key.</param>
/// <param name="Name">The operator-set Server name.</param>
/// <param name="Description">The optional Server description.</param>
/// <param name="Host">The owning Agent's id (<c>agt-</c>) as a string.</param>
/// <param name="RunState">The last-reported coarse run-state (drives the StatusBadge).</param>
/// <param name="Version">The installed build id, or <c>—</c> when unknown.</param>
/// <param name="CpuPercent">The latest CPU sample (0–100), or <c>null</c> when no sample is cached.</param>
/// <param name="MemoryUsedBytes">The latest resident-memory sample, or <c>null</c> when none is cached.</param>
/// <param name="MemoryLimitBytes">The container's memory limit for the sample, or <c>null</c>.</param>
public sealed record FleetRow(
    string Id,
    string Name,
    string? Description,
    string Host,
    ServerRunState RunState,
    string Version,
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? MemoryLimitBytes);
