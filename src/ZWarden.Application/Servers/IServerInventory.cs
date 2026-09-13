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

    /// <summary>Adopts a discovered container as a Server (fail-closed: re-checks <c>Server.Register</c>, the
    /// Agent's existence, and that the id was actually discovered). Idempotent on an already-imported id.</summary>
    Task<ServerImportResult> ImportAsync(
        UserId user,
        AgentId agentId,
        ServerId serverId,
        string name,
        CancellationToken cancellationToken = default);
}
