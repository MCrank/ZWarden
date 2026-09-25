using ZWarden.Domain.Servers;

namespace ZWarden.Web.Components.Pages.Servers;

/// <summary>
/// One row of the Fleet board (#158), projected by the static-SSR parent (which has the authorized, tenant-
/// filtered load and an <c>HttpContext</c>) and handed to the interactive <see cref="FleetBoard"/> island as a
/// parameter. It must be a public, serializable type because it crosses the prerender→interactive boundary.
/// It carries the last-reported registry metadata plus the first-render values of the fleet facts (#257,
/// <c>FleetFacts</c>); live-status.js keeps the Status, Players, CPU, Memory, Uptime and Version cells current
/// from the batched status poll. All Agent-reported text is untrusted and rendered as data (trust-boundaries §3/§8).
/// </summary>
/// <param name="Id">The Server's canonical id (<c>srv-</c>) as a string — the row-click target and cache key.</param>
/// <param name="Name">The operator-set Server name.</param>
/// <param name="Description">The optional Server description.</param>
/// <param name="Host">The owning Agent's id (<c>agt-</c>) as a string.</param>
/// <param name="RunState">The last-reported coarse run-state (the Status column's sort key).</param>
/// <param name="StatusLabel">The badge label at load, resolved against the in-flight Operation (#253), e.g.
/// <c>RESTARTING</c>; live-status.js keeps it current.</param>
/// <param name="StatusTone">The run-state whose colour the badge wears at load (#253).</param>
/// <param name="Version">The installed build id, or <c>—</c> when unknown.</param>
/// <param name="CpuPercent">The latest CPU sample (0–100), or <c>null</c> when no sample is cached.</param>
/// <param name="MemoryUsedBytes">The latest resident-memory sample, or <c>null</c> when none is cached.</param>
/// <param name="MemoryLimitBytes">The container's memory limit for the sample, or <c>null</c>.</param>
/// <param name="Players">Connected players from the last RCON sample, or <c>null</c> (renders <c>—</c>).</param>
/// <param name="PlayersAge">How old that sample is, e.g. <c>as of 2 min ago</c> (the cell's tooltip), or <c>null</c>.</param>
/// <param name="Uptime">The compact container uptime, e.g. <c>2h 14m</c>, or <c>—</c>.</param>
public sealed record FleetRow(
    string Id,
    string Name,
    string? Description,
    string Host,
    ServerRunState RunState,
    string StatusLabel,
    ServerRunState StatusTone,
    string Version,
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? MemoryLimitBytes,
    int? Players = null,
    string? PlayersAge = null,
    string Uptime = "—");
