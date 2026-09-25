using System.Globalization;
using ZWarden.Application.Servers;
using ZWarden.Domain.Servers;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// One Server's fleet-board facts (#257): what the Players / CPU / Memory / Uptime / Version columns and the KPI
/// tiles read. Computed by <see cref="FleetFacts.For"/> for both the first render and the batched status poll, so
/// the two never disagree. All values are observed Agent data (trust-boundaries.md §3), rendered as data.
/// </summary>
/// <param name="Players">Connected players from the Agent's last RCON sample, or <c>null</c>.</param>
/// <param name="PlayersSampledAt">When <paramref name="Players"/> was read (UTC), or <c>null</c>.</param>
/// <param name="StartedAt">The container's start time (UTC) — uptime is <c>now − StartedAt</c> — or <c>null</c>.</param>
/// <param name="Version">What the Version column shows: the game version (e.g. <c>42.20.4</c>, #262) when known, else
/// the Steam build id, else <c>null</c>.</param>
/// <param name="SteamBuild">The installed Steam build id (the Version cell's tooltip), or <c>null</c>.</param>
/// <param name="CpuPercent">The latest CPU sample (0–100), or <c>null</c>.</param>
/// <param name="MemoryUsedBytes">The latest resident-memory sample, or <c>null</c>.</param>
/// <param name="MemoryLimitBytes">The container's memory limit for the sample, or <c>null</c>.</param>
/// <param name="IsRunning">The last-reported run-state is Running (the Running tile).</param>
/// <param name="NeedsAttention">Health Failed/Degraded or run-state Failed (the Needs attention tile).</param>
public sealed record FleetServerFacts(
    int? Players,
    DateTimeOffset? PlayersSampledAt,
    DateTimeOffset? StartedAt,
    string? Version,
    string? SteamBuild,
    double? CpuPercent,
    long? MemoryUsedBytes,
    long? MemoryLimitBytes,
    bool IsRunning,
    bool NeedsAttention);

/// <summary>The fleet KPI tiles (#257).</summary>
/// <param name="Running">How many visible Servers last reported Running.</param>
/// <param name="NeedsAttention">How many need attention.</param>
/// <param name="AttentionName">The first such Server's name (untrusted, rendered as data), or <c>null</c>.</param>
/// <param name="PlayersOnline">The sum over Servers with a player count, or <c>null</c> when none has one.</param>
public sealed record FleetKpis(int Running, int NeedsAttention, string? AttentionName, int? PlayersOnline);

/// <summary>
/// Projects the fleet board's facts and KPI tiles (#257) from the persisted <see cref="ServerSummary"/> and the
/// ownership-guarded metrics sample. Players and uptime are blanked while the owning Agent is offline — a count or
/// an uptime from a host we can't hear from would be a guess. The status poll sends <see cref="FormatUptime"/> and
/// <see cref="FormatSampleAge"/> preformatted (server clock); <c>live-status.js</c> mirrors only <see cref="Kpis"/>
/// — keep the two in step.
/// </summary>
public static class FleetFacts
{
    /// <summary>The placeholder for a fact with no value.</summary>
    public const string Dash = "—";

    /// <summary>One Server's facts. <paramref name="sample"/> must already be ownership-guarded (read with the
    /// Server's owning Agent).</summary>
    public static FleetServerFacts For(ServerSummary server, ServerMetrics? sample, bool agentOnline)
    {
        ArgumentNullException.ThrowIfNull(server);
        bool live = agentOnline && sample is not null;
        // The persisted value wins; the cached sample fills in until the first report has been stored.
        string? build = FirstKnown(server.InstalledBuildId, sample?.InstalledBuildId);
        return new FleetServerFacts(
            live ? sample!.PlayerCount : null,
            live && sample!.PlayerCount is not null ? sample.PlayerCountSampledAt : null,
            live ? sample!.StartedAt : null,
            FirstKnown(server.GameVersion, sample?.GameVersion) ?? build,
            build,
            sample?.CpuPercent,
            sample?.MemoryUsedBytes,
            sample?.MemoryLimitBytes,
            server.LastRunState == ServerRunState.Running,
            server.LastHealth is ServerHealth.Failed or ServerHealth.Degraded || server.LastRunState == ServerRunState.Failed);
    }

    /// <summary>The Version cell's tooltip (#262): the Steam build id behind the shown game version, or a note that
    /// the game version has not been read yet; <c>null</c> when there is nothing to add.</summary>
    public static string? FormatVersionTitle(FleetServerFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.SteamBuild is null)
        {
            return null;
        }

        return facts.Version == facts.SteamBuild
            ? "Steam build id (the game version has not been read yet)"
            : Invariant($"Steam build {facts.SteamBuild}");
    }

    /// <summary>The KPI tiles over the visible fleet, in board order (the attention name is the first match).</summary>
    public static FleetKpis Kpis(IReadOnlyList<(ServerSummary Server, FleetServerFacts Facts)> fleet)
    {
        ArgumentNullException.ThrowIfNull(fleet);
        List<int> counts = fleet.Where(f => f.Facts.Players is not null).Select(f => f.Facts.Players!.Value).ToList();
        (ServerSummary Server, FleetServerFacts Facts)? firstAttention = fleet.FirstOrDefault(f => f.Facts.NeedsAttention);
        return new FleetKpis(
            fleet.Count(f => f.Facts.IsRunning),
            fleet.Count(f => f.Facts.NeedsAttention),
            fleet.Any(f => f.Facts.NeedsAttention) ? firstAttention!.Value.Server.Name : null,
            counts.Count == 0 ? null : counts.Sum());
    }

    /// <summary>Compact uptime: <c>&lt;1m</c>, <c>14m</c>, <c>2h 14m</c>, <c>3d 4h</c>; <see cref="Dash"/> with no
    /// start time. A start in the future (clock skew) reads <c>&lt;1m</c>.</summary>
    public static string FormatUptime(DateTimeOffset? startedAt, DateTimeOffset now)
    {
        if (startedAt is not { } started)
        {
            return Dash;
        }

        long minutes = (long)Math.Floor((now - started).TotalMinutes);
        if (minutes < 1)
        {
            return "<1m";
        }

        long days = minutes / 1440;
        long hours = minutes % 1440 / 60;
        long mins = minutes % 60;
        return days > 0 ? Invariant($"{days}d {hours}h")
            : hours > 0 ? Invariant($"{hours}h {mins}m")
            : Invariant($"{mins}m");
    }

    /// <summary>How old the player sample is, as a tooltip phrase: <c>as of just now</c>, <c>as of 2 min ago</c>,
    /// <c>as of 2 h ago</c>.</summary>
    public static string FormatSampleAge(DateTimeOffset sampledAt, DateTimeOffset now)
    {
        long minutes = (long)Math.Floor((now - sampledAt).TotalMinutes);
        return minutes < 1 ? "as of just now"
            : minutes < 60 ? Invariant($"as of {minutes} min ago")
            : Invariant($"as of {minutes / 60} h ago");
    }

    private static string? FirstKnown(string? persisted, string? cached) =>
        !string.IsNullOrWhiteSpace(persisted) ? persisted : string.IsNullOrWhiteSpace(cached) ? null : cached;

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);
}
