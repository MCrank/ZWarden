using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// The operator-facing inventory surface (F14). Reads are authorized <b>per Server</b> and fail-closed
/// (ADR 0018): a caller sees only the Servers it may <c>Server.View</c> — a tenant-wide grant sees all, a
/// server-scoped grant sees only its Servers. Import is authorized tenant-wide by <c>Server.Register</c>
/// and validates the target against what the Agent actually discovered. Everything is tenant-scoped
/// (ADR 0016).
/// </summary>
public interface IServerInventory
{
    /// <summary>The Servers the caller may view, most-recent first.</summary>
    Task<IReadOnlyList<ServerSummary>> ListVisibleAsync(UserId user, CancellationToken cancellationToken = default);

    /// <summary>One Server the caller may view, or <c>null</c> if absent or not visible to the caller.</summary>
    Task<ServerSummary?> GetVisibleAsync(UserId user, ServerId server, CancellationToken cancellationToken = default);

    /// <summary>The Agent's discovered canonical containers that are not yet registered as Servers — the
    /// import picker's offerings.</summary>
    Task<IReadOnlyList<DiscoveredServer>> ListDiscoveredUnregisteredAsync(
        AgentId agentId,
        CancellationToken cancellationToken = default);

    /// <summary>Every discovered-but-unregistered container across all Agents that have reported a snapshot —
    /// the inventory dashboard's import picker.</summary>
    Task<IReadOnlyList<DiscoveredServerOnAgent>> ListAllDiscoveredUnregisteredAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Adopts a discovered container as a Server (fail-closed: re-checks <c>Server.Register</c>, the
    /// Agent's existence, and that the id was actually discovered). Idempotent on an already-imported id.</summary>
    Task<ServerImportResult> ImportAsync(
        UserId user,
        AgentId agentId,
        ServerId serverId,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Registers a new Server on an Agent and enqueues the provisioning Operation that creates its
    /// canonical container (F14 PR-B; fail-closed: re-checks <c>Server.Register</c> and the Agent's existence).
    /// The Server starts in <c>Unknown</c> state; the Agent reports its ports and container id on completion. An optional
    /// <paramref name="gamePort"/> chooses the host pair (#229; out of range ⇒ <c>InvalidPort</c>, overlapping another
    /// Server on the host ⇒ <c>PortInUse</c>); <c>null</c> lets the Agent allocate the next free stride.</summary>
    Task<ServerRegisterResult> RegisterAsync(
        UserId user,
        AgentId agentId,
        string name,
        int? gamePort = null,
        CancellationToken cancellationToken = default);

    /// <summary>Registers a new Server from the new-server wizard (#230) — as the port-only overload, plus the heap, the
    /// initial settings (validated; the password encrypted before it is stored) and the capacity check: when the Agent's
    /// latest report says the new limit exceeds the host's free memory, it is refused (<c>OverCapacity</c>) unless the
    /// operator acknowledged it. No report yet ⇒ no check.</summary>
    Task<ServerRegisterResult> RegisterAsync(UserId user, NewServerRequest request, CancellationToken cancellationToken = default);
}
