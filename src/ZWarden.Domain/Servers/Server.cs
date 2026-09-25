using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Servers;

/// <summary>
/// A <b>Server</b> (<c>srv-</c>) — one Project Zomboid instance under ZWarden's management (CONTEXT.md).
/// A tenant-owned, versioned record that belongs to exactly one <see cref="Agents.Agent"/> (the
/// Agent-to-Server association, set once at import/registration and never moved in v1.0). It carries
/// operator metadata (name, description), the observed container linkage (the two allocated UDP ports and
/// the last-observed Docker id), and the coarse <b>last-reported</b> run-state — observed from an Agent
/// state snapshot, never inferred (trust-boundaries.md §3). The <see cref="Id"/> is the durable link to
/// the container: it is stamped onto the container as <c>io.zwarden.server-id</c> (F13), so the mutable
/// Docker id is only an observed convenience, never the key. Health <i>beyond</i> last-reported state,
/// and lifecycle (start/stop/restart), are later features (F16/F15). It is <see cref="ITenantOwned"/>
/// (stamped and filtered by the ambient tenant, ADR 0016) and <see cref="IVersioned"/>.
/// </summary>
public sealed class Server : IVersioned, ITenantOwned
{
    /// <summary>EF / factory use.</summary>
    public Server()
    {
    }

    /// <summary>The Server identifier (<c>srv-&lt;uuid&gt;</c>). Also the container's <c>io.zwarden.server-id</c>
    /// label — the durable link between the record and its container.</summary>
    public ServerId Id { get; init; } = ServerId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The Agent (host) this Server runs on — the Agent-to-Server association. Required and immutable
    /// in v1.0 (a Server is not moved between hosts).</summary>
    public AgentId AgentId { get; init; }

    /// <summary>The operator-facing name. Never a secret; not the container name.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>An optional operator-facing description; never a secret.</summary>
    public string? Description { get; private set; }

    /// <summary>The allocated game UDP port (PRD 28), once provisioned or observed; <c>null</c> until then.</summary>
    public int? GamePort { get; private set; }

    /// <summary>The allocated query/direct-connect UDP port (PRD 28), once provisioned or observed;
    /// <c>null</c> until then.</summary>
    public int? QueryPort { get; private set; }

    /// <summary>The last-observed Docker container id (trust-boundaries.md §8 — Agent-reported data). A
    /// convenience for diagnostics only; it changes on recreate, so it is never the key — <see cref="Id"/>
    /// is. <c>null</c> until a container is observed.</summary>
    public string? DockerContainerId { get; private set; }

    /// <summary>The coarse last-reported run-state as ZWarden.Web last recorded it from a snapshot. Observed,
    /// never inferred — it changes only via <see cref="RecordObservedState"/> from a fresh observation.</summary>
    public ServerRunState LastRunState { get; private set; } = ServerRunState.Unknown;

    /// <summary>When <see cref="LastRunState"/> was last reported (UTC); <c>null</c> until the first snapshot.
    /// The age of this timestamp is how an operator tells "Stopped" from "not heard from in an hour" — a
    /// first-class health signal in F16.</summary>
    public DateTimeOffset? LastStateReportedAt { get; private set; }

    /// <summary>The last-reported hierarchical <b>health</b> rollup (F16), as ZWarden.Web last recorded it from
    /// an Agent report. <c>null</c> until the first health report — "not heard from" is the age of
    /// <see cref="LastHealthReportedAt"/>, not an enum value. Observed, never inferred — changes only via
    /// <see cref="RecordObservedHealth"/>.</summary>
    public ServerHealth? LastHealth { get; private set; }

    /// <summary>When <see cref="LastHealth"/> was last reported (UTC); <c>null</c> until the first health report.</summary>
    public DateTimeOffset? LastHealthReportedAt { get; private set; }

    /// <summary>The Steam build id installed for this Server, as last observed after a SteamCMD update (F17), or
    /// <c>null</c> until an update reports one. Observed, never inferred — changes only via
    /// <see cref="RecordObservedBuild"/>.</summary>
    public string? InstalledBuildId { get; private set; }

    /// <summary>When <see cref="InstalledBuildId"/> was last reported (UTC); <c>null</c> until the first update.</summary>
    public DateTimeOffset? InstalledBuildReportedAt { get; private set; }

    /// <summary>The Project Zomboid game version (e.g. <c>42.20.4</c>) the server last printed at boot (#262), or
    /// <c>null</c> until an Agent reports one. Observed, never inferred — changes only via
    /// <see cref="RecordObservedGameVersion"/>.</summary>
    public string? GameVersion { get; private set; }

    /// <summary>When the Server was brought under management (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>
    /// <b>Imports</b> a Server: adopts a canonical container the Agent already discovered by taking the
    /// <see cref="ServerId"/> the container already carries (<c>io.zwarden.server-id</c>) as this record's
    /// id. The <see cref="TenantId"/> is left unset so the ownership interceptor stamps the ambient tenant
    /// on insert (ADR 0016). Run-state stays <see cref="ServerRunState.Unknown"/> until the next snapshot
    /// reconciles it.
    /// </summary>
    public static Server Import(
        AgentId agentId,
        ServerId serverId,
        string name,
        DateTimeOffset now,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (agentId.IsEmpty)
        {
            throw new ArgumentException("An imported Server must be bound to an Agent.", nameof(agentId));
        }

        if (serverId.IsEmpty)
        {
            throw new ArgumentException("An imported Server must adopt the discovered Server id.", nameof(serverId));
        }

        return new Server
        {
            Id = serverId,
            AgentId = agentId,
            Name = name,
            Description = description,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// <b>Registers</b> a new Server: mints a fresh <see cref="ServerId"/> for a Server whose canonical
    /// container does not exist yet — a provisioning Operation (F14 PR-B) creates it, stamped with this id.
    /// Run-state starts <see cref="ServerRunState.Unknown"/> and the ports/container id are recorded once the
    /// Agent reports them. The <see cref="TenantId"/> is left unset for the ownership interceptor (ADR 0016).
    /// </summary>
    public static Server Register(AgentId agentId, string name, DateTimeOffset now, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (agentId.IsEmpty)
        {
            throw new ArgumentException("A registered Server must be bound to an Agent.", nameof(agentId));
        }

        return new Server
        {
            Id = ServerId.New(),
            AgentId = agentId,
            Name = name,
            Description = description,
            CreatedAt = now,
        };
    }

    /// <summary>Records a freshly observed run-state and the time it was reported (trust-boundaries.md §3 —
    /// observed, never inferred).</summary>
    public void RecordObservedState(ServerRunState state, DateTimeOffset reportedAt)
    {
        LastRunState = state;
        LastStateReportedAt = reportedAt;
    }

    /// <summary>Records a freshly observed health rollup and the time it was reported (F16; trust-boundaries.md
    /// §3 — observed, never inferred).</summary>
    public void RecordObservedHealth(ServerHealth health, DateTimeOffset reportedAt)
    {
        LastHealth = health;
        LastHealthReportedAt = reportedAt;
    }

    /// <summary>Records the Steam build id observed after a SteamCMD update (F17; trust-boundaries.md §3 —
    /// observed, never inferred). A <c>null</c> build id (the update succeeded but the manifest could not be
    /// read) still stamps the reported time, so "when did we last update?" stays answerable.</summary>
    public void RecordObservedBuild(string? buildId, DateTimeOffset reportedAt)
    {
        InstalledBuildId = buildId;
        InstalledBuildReportedAt = reportedAt;
    }

    /// <summary>Records the game version an Agent read from the server's boot log (#262; trust-boundaries.md §3 —
    /// observed, never inferred).</summary>
    public void RecordObservedGameVersion(string gameVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameVersion);
        GameVersion = gameVersion;
    }

    /// <summary>Records the observed container linkage: its Docker id and the two allocated UDP ports. RCON
    /// is never published, so it is not carried here (F12/F13).</summary>
    public void RecordContainer(string dockerContainerId, int gamePort, int queryPort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dockerContainerId);
        ArgumentOutOfRangeException.ThrowIfLessThan(gamePort, 1, nameof(gamePort));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gamePort, 65535, nameof(gamePort));
        ArgumentOutOfRangeException.ThrowIfLessThan(queryPort, 1, nameof(queryPort));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(queryPort, 65535, nameof(queryPort));
        if (gamePort == queryPort)
        {
            throw new ArgumentException("The game and query ports must differ.", nameof(queryPort));
        }

        DockerContainerId = dockerContainerId;
        GamePort = gamePort;
        QueryPort = queryPort;
    }

    /// <summary>Renames the Server and updates its description. Metadata only; does not touch the container.</summary>
    public void Rename(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Description = description;
    }
}
